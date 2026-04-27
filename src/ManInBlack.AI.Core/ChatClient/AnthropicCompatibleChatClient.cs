using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Core;

/// <summary>
/// Anthropic 兼容 API 适配器，实现 IChatClient 接口
/// 可连接任何兼容 Anthropic API 形状的接口，支持 tool calling
/// </summary>
public sealed class AnthropicCompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly string _endPoint;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AnthropicCompatibleChatClient(
        HttpClient httpClient,
        string modelId = "claude-sonnet-4-20250514")
    {
        _httpClient = httpClient;
        _modelId = modelId;
        _endPoint = "v1/messages";
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(messages, options, stream: false);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_endPoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {errorBody}");
        }

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
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // 累积 tool_use 分片：index → (id, name, partialJson)
        var toolUseBlocks = new Dictionary<int, (string Id, string Name, StringBuilder PartialJson)>();
        long? inputTokens = null;
        long? outputTokens = null;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var jsonStr = line[6..];

            JsonNode? node = null;
            try { node = JsonNode.Parse(jsonStr); }
            catch { }
            if (node is null) continue;

            var type = node["type"]?.GetValue<string>();

            // 文本增量
            if (type == "content_block_delta")
            {
                var delta = node["delta"];
                var deltaType = delta?["type"]?.GetValue<string>();
                var index = node["index"]?.GetValue<int>() ?? 0;

                if (deltaType == "text_delta")
                {
                    var text = delta?["text"]?.GetValue<string>();
                    if (text is not null)
                    {
                        yield return new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(text)]
                        };
                    }
                }
                else if (deltaType == "input_json_delta")
                {
                    var partial = delta?["partial_json"]?.GetValue<string>() ?? "";
                    if (toolUseBlocks.TryGetValue(index, out var existing))
                        toolUseBlocks[index] = (existing.Id, existing.Name, existing.PartialJson.Append(partial));
                }
            }
            // tool_use 块开始
            else if (type == "content_block_start")
            {
                var contentBlock = node["content_block"];
                var blockType = contentBlock?["type"]?.GetValue<string>();
                var index = node["index"]?.GetValue<int>() ?? 0;

                if (blockType == "tool_use")
                {
                    var id = contentBlock?["id"]?.GetValue<string>() ?? "";
                    var name = contentBlock?["name"]?.GetValue<string>() ?? "";
                    toolUseBlocks[index] = (id, name, new StringBuilder());
                }
            }
            // content_block_stop → 输出累积的 tool call
            else if (type == "content_block_stop")
            {
                var index = node["index"]?.GetValue<int>() ?? 0;
                if (toolUseBlocks.TryGetValue(index, out var tc))
                {
                    var args = ParseArguments(tc.PartialJson.ToString());
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new FunctionCallContent(tc.Id, tc.Name, args)]
                    };
                    toolUseBlocks.Remove(index);
                }
            }
            // message_start → 提取 input_tokens
            else if (type == "message_start")
            {
                var usage = node["message"]?["usage"];
                if (usage is not null)
                    inputTokens = usage["input_tokens"]?.GetValue<int>();
            }
            // message_delta → 提取 output_tokens
            else if (type == "message_delta")
            {
                var usage = node["usage"];
                if (usage is not null)
                    outputTokens = usage["output_tokens"]?.GetValue<int>();
            }
        }

        // 流结束后输出 usage
        if (inputTokens is not null || outputTokens is not null)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new UsageContent(new UsageDetails
                {
                    InputTokenCount = inputTokens,
                    OutputTokenCount = outputTokens,
                    TotalTokenCount = (inputTokens ?? 0) + (outputTokens ?? 0)
                })]
            };
        }
    }

    private string BuildRequestBody(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var messageList = messages.ToList();

        var systemText = string.Join("\n", messageList
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text)
            .Where(t => !string.IsNullOrEmpty(t)));

        var body = new JsonObject
        {
            ["model"] = _modelId,
            ["max_tokens"] = options?.MaxOutputTokens ?? 4096,
            ["top_p"] = (double?)options?.TopP,
            ["system"] = string.IsNullOrEmpty(systemText) ? null : systemText,
            ["messages"] = SerializeMessages(messageList.Where(m => m.Role != ChatRole.System))
        };

        if (options?.Temperature is not null)
            body["temperature"] = (double?)options.Temperature;

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
            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            var functionResults = msg.Contents.OfType<FunctionResultContent>().ToList();

            if (functionCalls.Count > 0)
            {
                // assistant 消息含 tool_use
                var contentArray = new JsonArray();

                // 保留 assistant 的文本内容
                if (!string.IsNullOrEmpty(msg.Text))
                {
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = msg.Text
                    });
                }

                foreach (var fc in functionCalls)
                {
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = fc.CallId!,
                        ["name"] = fc.Name!,
                        ["input"] = fc.Arguments!.Count > 0
                            ? JsonNode.Parse(JsonSerializer.Serialize(fc.Arguments))!
                            : new JsonObject()
                    });
                }

                array.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = contentArray
                });
            }
            else if (functionResults.Count > 0)
            {
                // user 消息含 tool_result
                var contentArray = new JsonArray();
                foreach (var fr in functionResults)
                {
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = fr.CallId,
                        ["content"] = fr.Result?.ToString()
                    });
                }

                array.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = contentArray
                });
            }
            else
            {
                array.Add(new JsonObject
                {
                    ["role"] = msg.Role.ToString().ToLower(),
                    ["content"] = msg.Text
                });
            }
        }
        return array;
    }

    private static JsonArray SerializeTools(IList<AITool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            var obj = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description ?? ""
            };

            if (tool is AIFunction aiFunc)
                obj["input_schema"] = JsonNode.Parse(aiFunc.JsonSchema.GetRawText());
            else
                obj["input_schema"] = new JsonObject { ["type"] = "object" };

            array.Add(obj);
        }
        return array;
    }

    private ChatResponse ParseResponse(string responseJson)
    {
        var result = JsonSerializer.Deserialize<AnthropicResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Failed to parse Anthropic-compatible response with model {_modelId} to url {_httpClient.BaseAddress}");

        var contents = new List<AIContent>();

        if (result.Content is not null)
        {
            foreach (var block in result.Content)
            {
                if (block.Type == "text" && block.Text is not null)
                    contents.Add(new TextContent(block.Text));
                else if (block.Type == "tool_use")
                    contents.Add(new FunctionCallContent(
                        block.Id ?? "",
                        block.Name ?? "",
                        ParseArguments(block.Input?.ToString() ?? "{}")));
            }
        }

        var chatMessage = new ChatMessage(ChatRole.Assistant, contents);
        var chatResponse = new ChatResponse(new List<ChatMessage> { chatMessage });

        if (result.Usage is not null)
        {
            chatResponse.Usage = new UsageDetails
            {
                InputTokenCount = result.Usage.InputTokens,
                OutputTokenCount = result.Usage.OutputTokens,
                TotalTokenCount = result.Usage.InputTokens + result.Usage.OutputTokens
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

    private class AnthropicResponse
    {
        public List<AnthropicContentBlock>? Content { get; set; }
        public AnthropicUsage? Usage { get; set; }
        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }
    }

    private class AnthropicContentBlock
    {
        public string Type { get; set; } = "";
        public string? Text { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public JsonElement? Input { get; set; }
    }

    private class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }

    #endregion
}
