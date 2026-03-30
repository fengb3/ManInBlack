using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// OpenAI 兼容 API 适配器，实现 IChatClient 接口
/// 可连接任何兼容 OpenAI API 形状的接口，支持 tool calling
/// </summary>
public sealed class OpenAICompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly string _endPoint;

    public OpenAICompatibleChatClient(HttpClient httpClient, string modelId)
    {
        _httpClient = httpClient;
        _modelId = modelId;
        _endPoint = "v1/chat/completions";
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(messages, options, stream: false);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_endPoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResponse(responseJson);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(messages, options, stream: true);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, _endPoint) { Content = content };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // 累积 tool call 分片
        var toolCalls = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var jsonStr = line[6..];
            if (jsonStr == "[DONE]")
                break;

            OpenAIStreamChunk? chunk = null;
            try { chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(jsonStr); }
            catch { }

            var choice = chunk?.Choices?.FirstOrDefault();
            if (choice is null) continue;

            // 文本内容
            if (choice.Delta?.Content is not null)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(choice.Delta.Content)]
                };
            }

            // tool call 分片累积
            if (choice.Delta?.ToolCalls is not null)
            {
                foreach (var tc in choice.Delta.ToolCalls)
                {
                    if (!toolCalls.TryGetValue(tc.Index, out var existing))
                    {
                        toolCalls[tc.Index] = (
                            tc.Id ?? "",
                            tc.Function?.Name ?? "",
                            new StringBuilder(tc.Function?.Arguments ?? "")
                        );
                    }
                    else
                    {
                        var id = tc.Id ?? existing.Id;
                        var name = tc.Function?.Name ?? existing.Name;
                        var sb = existing.Arguments;
                        if (tc.Function?.Arguments is not null)
                            sb.Append(tc.Function.Arguments);
                        toolCalls[tc.Index] = (id, name, sb);
                    }
                }
            }

            // 完成时输出累积的 tool calls
            if (choice.FinishReason is "tool_calls" or "stop" && toolCalls.Count > 0)
            {
                foreach (var (_, tc) in toolCalls)
                {
                    var args = ParseArguments(tc.Arguments.ToString());
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new FunctionCallContent(tc.Id, tc.Name, args)]
                    };
                }
                toolCalls.Clear();
            }
        }
    }

    private string BuildRequestBody(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = _modelId,
            ["messages"] = SerializeMessages(messages),
            ["max_tokens"] = options?.MaxOutputTokens,
            ["temperature"] = (double?)options?.Temperature,
            ["top_p"] = (double?)options?.TopP
        };

        if (stream)
            body["stream"] = true;

        if (options?.Tools?.Count > 0)
            body["tools"] = SerializeTools(options.Tools);

        return body.ToJsonString();
    }

    private static JsonArray SerializeMessages(IEnumerable<ChatMessage> messages)
    {
        var array = new JsonArray();
        foreach (var msg in messages)
        {
            var obj = new JsonObject { ["role"] = msg.Role.ToString().ToLower() };

            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            var functionResults = msg.Contents.OfType<FunctionResultContent>().ToList();

            if (functionCalls.Count > 0)
            {
                var calls = new JsonArray();
                foreach (var fc in functionCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = fc.CallId,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = fc.Name,
                            ["arguments"] = JsonSerializer.Serialize(fc.Arguments)
                        }
                    });
                }
                obj["tool_calls"] = calls;
                obj["content"] = msg.Text;
            }
            else if (functionResults.Count > 0)
            {
                var result = functionResults[0];
                obj["tool_call_id"] = result.CallId;
                obj["content"] = result.Result?.ToString();
            }
            else
            {
                obj["content"] = msg.Text;
            }

            array.Add(obj);
        }
        return array;
    }

    private static JsonArray SerializeTools(IList<AITool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            var funcObj = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description ?? ""
            };

            if (tool is AIFunction aiFunc)
                funcObj["parameters"] = JsonNode.Parse(aiFunc.JsonSchema.GetRawText());
            else
                funcObj["parameters"] = new JsonObject { ["type"] = "object" };

            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = funcObj
            });
        }
        return array;
    }

    private ChatResponse ParseResponse(string responseJson)
    {
        var result = JsonSerializer.Deserialize<OpenAIResponse>(responseJson)
            ?? throw new InvalidOperationException("Failed to parse OpenAI-compatible response");

        var choice = result.Choices?.FirstOrDefault()
            ?? throw new InvalidOperationException("No choices in response");

        var message = choice.Message
            ?? throw new InvalidOperationException("No message in response");

        var contents = new List<AIContent>();

        if (!string.IsNullOrEmpty(message.Content))
            contents.Add(new TextContent(message.Content));

        if (message.ToolCalls?.Count > 0)
        {
            foreach (var tc in message.ToolCalls)
            {
                var args = ParseArguments(tc.Function.Arguments);
                contents.Add(new FunctionCallContent(tc.Id, tc.Function.Name, args));
            }
        }

        var chatMessage = new ChatMessage(ChatRole.Assistant, contents);
        var chatResponse = new ChatResponse(new List<ChatMessage> { chatMessage });

        if (result.Usage is not null)
        {
            chatResponse.AdditionalProperties["Usage"] = new
            {
                InputTokenCount = result.Usage.PromptTokens,
                OutputTokenCount = result.Usage.CompletionTokens,
                TotalTokenCount = result.Usage.PromptTokens + result.Usage.CompletionTokens
            };
        }

        return chatResponse;
    }

    private static Dictionary<string, object?> ParseArguments(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new(); }
        catch { return new(); }
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    #region JSON 模型

    private class OpenAIResponse
    {
        public List<OpenAIChoice>? Choices { get; set; }
        public OpenAIUsage? Usage { get; set; }
    }

    private class OpenAIChoice
    {
        public OpenAIMessage? Message { get; set; }
        public string? FinishReason { get; set; }
    }

    private class OpenAIMessage
    {
        public string? Content { get; set; }
        public List<OpenAIToolCall>? ToolCalls { get; set; }
    }

    private class OpenAIToolCall
    {
        public string Id { get; set; } = "";
        public OpenAIToolCallFunction Function { get; set; } = new();
    }

    private class OpenAIToolCallFunction
    {
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";
    }

    private class OpenAIUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }

    private class OpenAIStreamChunk
    {
        public List<OpenAIStreamChoice>? Choices { get; set; }
    }

    private class OpenAIStreamChoice
    {
        public OpenAIStreamDelta? Delta { get; set; }
        public string? FinishReason { get; set; }
    }

    private class OpenAIStreamDelta
    {
        public string? Content { get; set; }
        public List<OpenAIStreamToolCall>? ToolCalls { get; set; }
    }

    private class OpenAIStreamToolCall
    {
        public int Index { get; set; }
        public string? Id { get; set; }
        public OpenAIStreamToolCallFunction? Function { get; set; }
    }

    private class OpenAIStreamToolCallFunction
    {
        public string? Name { get; set; }
        public string? Arguments { get; set; }
    }

    #endregion
}
