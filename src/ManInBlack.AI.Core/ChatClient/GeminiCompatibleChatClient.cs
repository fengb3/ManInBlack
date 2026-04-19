using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Core;

/// <summary>
/// Google Gemini 兼容 API 适配器，实现 IChatClient 接口
/// 可连接任何兼容 Gemini API 形状的接口，支持 tool calling
/// </summary>
public sealed class GeminiCompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly string _blockedEndpoint;
    private readonly string _streamEndPoint;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GeminiCompatibleChatClient(
        HttpClient httpClient,
        string apiKey,
        string modelId = "gemini-2.5-flash-preview-04-17")
    {
        _httpClient = httpClient;
        _modelId = modelId;
        _apiKey = apiKey;
        _blockedEndpoint = "v1beta/models/{0}:generateContent?key={1}";
        _streamEndPoint = "v1beta/models/{0}:streamGenerateContent?key={1}&alt=sse";
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var endPoint = string.Format(_blockedEndpoint, _modelId, _apiKey);
        var body = BuildRequestBody(messages, options);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(endPoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResponse(responseJson);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var endPoint = string.Format(_streamEndPoint, _modelId, _apiKey);
        var body = BuildRequestBody(messages, options);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, endPoint) { Content = content };
        var response =
            await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var callIdCounter = 0;
        UsageDetails? lastUsage = null;

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var jsonStr = line[6..];
            if (jsonStr == "[DONE]")
                break;

            GeminiResponse? result = null;
            try { result = JsonSerializer.Deserialize<GeminiResponse>(jsonStr, JsonOptions); }
            catch { }

            // 追踪 usage（最后一个非 null 的为最终值）
            if (result?.UsageMetadata is not null)
            {
                lastUsage = new UsageDetails
                {
                    InputTokenCount = result.UsageMetadata.PromptTokenCount,
                    OutputTokenCount = result.UsageMetadata.CandidatesTokenCount,
                    TotalTokenCount = result.UsageMetadata.TotalTokenCount
                };
            }

            var parts = result?.Candidates?.FirstOrDefault()?.Content?.Parts;
            if (parts is null) continue;

            foreach (var part in parts)
            {
                if (part.Text is not null)
                {
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(part.Text)]
                    };
                }
                else if (part.FunctionCall is not null)
                {
                    var fc = part.FunctionCall;
                    var args = fc.Args.HasValue
                        ? JsonSerializer.Deserialize<Dictionary<string, object?>>(fc.Args.Value.GetRawText()) ?? new()
                        : new Dictionary<string, object?>();
                    var callId = $"call_{callIdCounter++}";
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new FunctionCallContent(callId, fc.Name ?? "", args)]
                    };
                }
            }
        }

        // 流结束后输出 usage
        if (lastUsage is not null)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new UsageContent(lastUsage)]
            };
        }
    }

    private string BuildRequestBody(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var body = new JsonObject
        {
            ["contents"] = SerializeMessages(messages),
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = options?.MaxOutputTokens ?? 4096,
                ["temperature"] = (double?)(options?.Temperature ?? 0.7),
                ["topP"] = (double?)options?.TopP
            }
        };

        if (options?.Tools?.Count > 0)
            body["tools"] = SerializeTools(options.Tools);

        return body.ToJsonString();
    }

    private static JsonArray SerializeMessages(IEnumerable<ChatMessage> messages)
    {
        var array = new JsonArray();
        // 追踪 CallId → Name，用于 functionResponse 查找函数名
        var callIdToName = new Dictionary<string, string>();

        foreach (var msg in messages)
        {
            // 记录 function call 的 CallId → Name
            foreach (var fc in msg.Contents.OfType<FunctionCallContent>())
                callIdToName[fc.CallId] = fc.Name;

            if (msg.Role == ChatRole.System)
            {
                array.Add(SerializeSingleMessage("user", msg, callIdToName));
                continue;
            }

            var role = msg.Role == ChatRole.Assistant ? "model" : "user";
            array.Add(SerializeSingleMessage(role, msg, callIdToName));
        }

        return array;
    }

    private static JsonObject SerializeSingleMessage(string role, ChatMessage msg, Dictionary<string, string> callIdToName)
    {
        var parts = new JsonArray();

        // 文本内容
        var text = msg.Text;
        if (!string.IsNullOrEmpty(text))
            parts.Add(new JsonObject { ["text"] = text });

        // function call（assistant 消息中的工具调用）
        foreach (var fc in msg.Contents.OfType<FunctionCallContent>())
        {
            var argsJson = fc.Arguments!.Count > 0
                ? JsonSerializer.Serialize(fc.Arguments)
                : "{}";
            parts.Add(new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["name"] = fc.Name!,
                    ["args"] = JsonNode.Parse(argsJson)!
                }
            });
        }

        // function response（user 消息中的工具结果）
        foreach (var fr in msg.Contents.OfType<FunctionResultContent>())
        {
            var funcName = callIdToName.TryGetValue(fr.CallId, out var name) ? name : fr.CallId;
            parts.Add(new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = funcName,
                    ["response"] = new JsonObject
                    {
                        ["result"] = fr.Result?.ToString() ?? ""
                    }
                }
            });
        }

        if (parts.Count == 0)
            parts.Add(new JsonObject { ["text"] = "" });

        return new JsonObject
        {
            ["role"] = role,
            ["parts"] = parts
        };
    }

    private static JsonArray SerializeTools(IList<AITool> tools)
    {
        var declarations = new JsonArray();
        foreach (var tool in tools)
        {
            var obj = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description ?? ""
            };

            if (tool is AIFunction aiFunc)
                obj["parameters"] = JsonNode.Parse(aiFunc.JsonSchema.GetRawText());
            else
                obj["parameters"] = new JsonObject { ["type"] = "object" };

            declarations.Add(obj);
        }

        // Gemini tools 格式：[{ functionDeclarations: [...] }]
        return new JsonArray
        {
            new JsonObject { ["functionDeclarations"] = declarations }
        };
    }

    private ChatResponse ParseResponse(string responseJson)
    {
        var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson, JsonOptions)
                     ?? throw new InvalidOperationException("Failed to parse Gemini-compatible response");

        var candidate = result.Candidates?.FirstOrDefault()
                        ?? throw new InvalidOperationException("No candidates in response");

        var contents = new List<AIContent>();

        if (candidate.Content?.Parts is not null)
        {
            var callIdCounter = 0;
            foreach (var part in candidate.Content.Parts)
            {
                if (part.Text is not null)
                    contents.Add(new TextContent(part.Text));
                else if (part.FunctionCall is not null)
                {
                    var fc = part.FunctionCall;
                    var args = fc.Args.HasValue
                        ? JsonSerializer.Deserialize<Dictionary<string, object?>>(fc.Args.Value.GetRawText()) ?? new()
                        : new Dictionary<string, object?>();
                    var callId = $"call_{callIdCounter++}";
                    contents.Add(new FunctionCallContent(callId, fc.Name ?? "", args));
                }
            }
        }

        var chatMessage = new ChatMessage(ChatRole.Assistant, contents);
        var chatResponse = new ChatResponse(new List<ChatMessage> { chatMessage });

        if (result.UsageMetadata is not null)
        {
            chatResponse.Usage = new UsageDetails
            {
                InputTokenCount = result.UsageMetadata.PromptTokenCount,
                OutputTokenCount = result.UsageMetadata.CandidatesTokenCount,
                TotalTokenCount = result.UsageMetadata.TotalTokenCount
            };
        }

        return chatResponse;
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    #region JSON 模型

    private class GeminiResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
        public string? FinishReason { get; set; }
    }

    private class GeminiContent
    {
        public List<GeminiPart>? Parts { get; set; }
    }

    private class GeminiPart
    {
        public string? Text { get; set; }
        public GeminiFunctionCall? FunctionCall { get; set; }
    }

    private class GeminiFunctionCall
    {
        public string? Name { get; set; }
        public JsonElement? Args { get; set; }
    }

    private class GeminiUsageMetadata
    {
        public int PromptTokenCount { get; set; }
        public int CandidatesTokenCount { get; set; }
        public int TotalTokenCount { get; set; }
    }

    #endregion
}
