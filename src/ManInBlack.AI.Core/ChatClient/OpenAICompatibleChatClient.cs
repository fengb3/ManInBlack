using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Core;

/// <summary>
/// OpenAI 兼容 API 适配器，实现 IChatClient 接口
/// 可连接任何兼容 OpenAI API 形状的接口，支持 tool calling
/// </summary>
public sealed class OpenAICompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly string _endPoint;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public OpenAICompatibleChatClient(HttpClient httpClient, string modelId)
    {
        _httpClient = httpClient;
        _modelId = modelId;
        _endPoint = "chat/completions";
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
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
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        var body = BuildRequestBody(messages, options, stream: true);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        // Console.WriteLine("Request body:");
        // Console.WriteLine(body); // Debug log

        var request = new HttpRequestMessage(HttpMethod.Post, _endPoint) { Content = content };
        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        // // print response body
        // Console.WriteLine("Response body:");
        // var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        // Console.WriteLine(responseBody);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // 累积 tool call 分片
        var toolCalls = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        UsageDetails? lastUsage = null;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            // Console.ForegroundColor = ConsoleColor.Yellow;
            // Console.BackgroundColor = ConsoleColor.DarkBlue;
            // Console.WriteLine($"{line}"); // Debug log
            // Console.ResetColor();

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var jsonStr = line[6..];
            if (jsonStr == "[DONE]")
                break;

            OpenAIStreamChunk? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(jsonStr, JsonOptions);
            }
            catch { }

            // 追踪 usage（最后一个非 null 的为最终值）
            if (chunk?.Usage is not null)
            {
                lastUsage = new UsageDetails
                {
                    InputTokenCount = chunk.Usage.PromptTokens,
                    OutputTokenCount = chunk.Usage.CompletionTokens,
                    TotalTokenCount = chunk.Usage.PromptTokens + chunk.Usage.CompletionTokens,
                };
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            if (choice is null)
                continue;

            // 文本内容
            if (choice.Delta?.Content is not null)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(choice.Delta.Content)],
                };
            }

            // 推理内容（reasoning_content）
            if (choice.Delta?.ReasoningContent is not null)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextReasoningContent(choice.Delta.ReasoningContent)],
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
                        Contents = [new FunctionCallContent(tc.Id, tc.Name, args)],
                    };
                }
                toolCalls.Clear();
            }
        }

        // 流结束后兜底输出未发送的 tool calls（部分提供商可能不发 finish_reason）
        if (toolCalls.Count > 0)
        {
            foreach (var (_, tc) in toolCalls)
            {
                var args = ParseArguments(tc.Arguments.ToString());
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new FunctionCallContent(tc.Id, tc.Name, args)],
                };
            }
        }

        // 流结束后输出 usage
        if (lastUsage is not null)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new UsageContent(lastUsage)],
            };
        }
    }

    private string BuildRequestBody(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        bool stream
    )
    {
        var body = new JsonObject
        {
            ["model"] = _modelId,
            ["messages"] = SerializeMessages(messages),
        };

        if (options?.MaxOutputTokens is not null)
            body["max_tokens"] = options.MaxOutputTokens;
        if (options?.Temperature is not null)
            body["temperature"] = (double?)options.Temperature;
        if (options?.TopP is not null)
            body["top_p"] = (double?)options.TopP;

        if (stream)
        {
            body["stream"] = true;
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

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
                    calls.Add(
                        new JsonObject
                        {
                            ["id"] = fc.CallId,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = fc.Name,
                                ["arguments"] = JsonSerializer.Serialize(fc.Arguments),
                            },
                        }
                    );
                }
                obj["tool_calls"] = calls;
                obj["content"] = msg.Text;
            }
            else if (functionResults.Count > 0)
            {
                // 每个 FunctionResultContent 生成独立的 tool 消息
                foreach (var result in functionResults)
                {
                    array.Add(
                        new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = result.CallId,
                            ["content"] = result.Result?.ToString(),
                        }
                    );
                }
                // 跳过外层的 array.Add(obj)，因为已经手动添加了
                continue;
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
                ["description"] = tool.Description ?? "",
            };

            if (tool is AIFunctionDeclaration aiFuncDecl)
                funcObj["parameters"] = JsonNode.Parse(aiFuncDecl.JsonSchema.GetRawText());
            else
                funcObj["parameters"] = new JsonObject { ["type"] = "object" };

            array.Add(new JsonObject { ["type"] = "function", ["function"] = funcObj });
        }
        return array;
    }

    private ChatResponse ParseResponse(string responseJson)
    {
        var result =
            JsonSerializer.Deserialize<OpenAIResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse OpenAI-compatible response");

        var choice =
            result.Choices?.FirstOrDefault()
            ?? throw new InvalidOperationException("No choices in response");

        var message =
            choice.Message ?? throw new InvalidOperationException("No message in response");

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
            chatResponse.Usage = new UsageDetails
            {
                InputTokenCount = result.Usage.PromptTokens,
                OutputTokenCount = result.Usage.CompletionTokens,
                TotalTokenCount = result.Usage.PromptTokens + result.Usage.CompletionTokens,
            };
        }

        return chatResponse;
    }

    private static Dictionary<string, object?> ParseArguments(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new();
        }
        catch
        {
            return new();
        }
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

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class OpenAIMessage
    {
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
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
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }

    private class OpenAIStreamChunk
    {
        public List<OpenAIStreamChoice>? Choices { get; set; }
        public OpenAIUsage? Usage { get; set; }
    }

    private class OpenAIStreamChoice
    {
        public OpenAIStreamDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class OpenAIStreamDelta
    {
        public string? Content { get; set; }

        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }

        [JsonPropertyName("tool_calls")]
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
