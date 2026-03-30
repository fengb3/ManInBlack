using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// Google Gemini 兼容 API 适配器，实现 IChatClient 接口
/// 可连接任何兼容 Gemini API 形状的接口
/// </summary>
public sealed class GeminiCompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly string _blockedEndpoint;
    private readonly string _streamEndPoint;

    /// <summary>
    /// 创建 Gemini 兼容聊天客户端
    /// </summary>
    /// <param name="httpClient">HttpClient 实例，通过依赖注入提供</param>
    /// <param name="apiKey">API 密钥</param>
    /// <param name="baseUrl">非流式 API 基础地址模板，其中 {0} 替换为模型 ID，{1} 替换为 API 密钥</param>
    /// <param name="streamBaseUrl">流式 API 基础地址模板，其中 {0} 替换为模型 ID，{1} 替换为 API 密钥</param>
    /// <param name="modelId">模型 ID</param>
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

        var requestBody = new
        {
            contents = messages.Select(m => new
            {
                role = m.Role == ChatRole.Assistant ? "model" : "user",
                parts = new[]
                {
                    new { text = m.Text }
                }
            }).ToArray(),
            generationConfig = new
            {
                maxOutputTokens = options?.MaxOutputTokens ?? 4096,
                temperature = (float?)(options?.Temperature ?? 0.7),
                topP = (float?)(options?.TopP)
            }
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(endPoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson)
                     ?? throw new InvalidOperationException("Failed to parse Gemini-compatible response");

        var candidate = result.Candidates?.FirstOrDefault()
                        ?? throw new InvalidOperationException("No candidates in response");

        var text = candidate.Content?.Parts?.FirstOrDefault()?.Text ?? "";

        var chatResponse = new ChatResponse(
            new List<ChatMessage> { new ChatMessage(ChatRole.Assistant, text) }
        );

        if (result.UsageMetadata is not null)
        {
#pragma warning disable CS8602
            var usage = result.UsageMetadata!;
            chatResponse.AdditionalProperties["Usage"] = new
            {
                InputTokenCount = usage.PromptTokenCount,
                OutputTokenCount = usage.CandidatesTokenCount,
                TotalTokenCount = usage.TotalTokenCount
            };
#pragma warning restore CS8602
        }

        return chatResponse;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var endPoint = string.Format(_streamEndPoint, _modelId, _apiKey);

        var requestBody = new
        {
            contents = messages.Select(m => new
            {
                role = m.Role == ChatRole.Assistant ? "model" : "user",
                parts = new[]
                {
                    new { text = m.Text }
                }
            }).ToArray(),
            generationConfig = new
            {
                maxOutputTokens = options?.MaxOutputTokens ?? 4096,
                temperature = (float?)(options?.Temperature ?? 0.7),
                topP = (float?)(options?.TopP)
            }
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, endPoint) { Content = content };
        var response =
            await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var jsonStr = line[6..];
            if (jsonStr == "[DONE]")
                break;

            GeminiResponse? result = null;
            try
            {
                result = JsonSerializer.Deserialize<GeminiResponse>(jsonStr);
            }
            catch
            {
            }

            var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                var update = new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(text)]
                };
                yield return update;
            }
        }
    }

    public void Dispose()
    {
        // HttpClient 由 DI 容器管理，不在此处释放
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    #region JSON 响应模型

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
    }

    private class GeminiUsageMetadata
    {
        public int PromptTokenCount { get; set; }
        public int CandidatesTokenCount { get; set; }
        public int TotalTokenCount { get; set; }
    }

    #endregion
}