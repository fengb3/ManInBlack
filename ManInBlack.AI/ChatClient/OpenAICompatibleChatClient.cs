using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// OpenAI 兼容 API 适配器，实现 IChatClient 接口
/// 可连接任何兼容 OpenAI API 形状的接口
/// </summary>
public sealed class OpenAICompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly string _endPoint;
    
    /// <summary>
    /// 创建 OpenAI 兼容聊天客户端
    /// </summary>
    /// <param name="httpClient">HttpClient 实例，通过依赖注入提供</param>
    /// <param name="apiKey">API 密钥</param>
    /// <param name="baseUrl">API 基础地址，默认为官方 OpenAI 地址</param>
    /// <param name="modelId">模型 ID</param>
    public OpenAICompatibleChatClient(
        HttpClient httpClient, string  modelId)
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
        var requestBody = new
        {
            model = _modelId,
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLower(),
                content = m.Text
            }).ToArray(),
            max_tokens = options?.MaxOutputTokens,
            temperature = (float?)(options?.Temperature),
            top_p = (float?)(options?.TopP)
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_endPoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<OpenAIResponse>(responseJson)
            ?? throw new InvalidOperationException("Failed to parse OpenAI-compatible response");

        var choice = result.Choices?.FirstOrDefault()
            ?? throw new InvalidOperationException("No choices in response");

        var responseMessage = new ChatMessage(ChatRole.Assistant, choice.Message?.Content ?? "");

        var chatResponse = new ChatResponse(new List<ChatMessage> { responseMessage });

        // 设置使用统计
        if (result.Usage is not null)
        {
#pragma warning disable CS8602
            var usage = result.Usage!;
            chatResponse.AdditionalProperties["Usage"] = new
            {
                InputTokenCount = usage.PromptTokens,
                OutputTokenCount = usage.CompletionTokens,
                TotalTokenCount = usage.PromptTokens + usage.CompletionTokens
            };
#pragma warning restore CS8602
        }

        return chatResponse;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            model = _modelId,
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLower(),
                content = m.Text
            }).ToArray(),
            max_tokens = options?.MaxOutputTokens,
            temperature = (float?)(options?.Temperature),
            top_p = (float?)(options?.TopP),
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        
        // Trim
        var request = new HttpRequestMessage(HttpMethod.Post, _endPoint) { Content = content };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            OpenAIStreamResponse? result = null;
            try
            {
                result = JsonSerializer.Deserialize<OpenAIStreamResponse>(jsonStr);
            }
            catch { }

            var delta = result?.Choices?.FirstOrDefault()?.Delta;
            if (delta?.Content is not null)
            {
                var update = new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(delta.Content)]
                };
                yield return update;
            }
        }
    }

    public void Dispose()
    {
        // 有依赖注入控制Dispose
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    #region JSON 响应模型

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
    }

    private class OpenAIUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }

    private class OpenAIStreamResponse
    {
        public List<OpenAIStreamChoice>? Choices { get; set; }
    }

    private class OpenAIStreamChoice
    {
        public OpenAIDelta? Delta { get; set; }
    }

    private class OpenAIDelta
    {
        public string? Content { get; set; }
    }

    #endregion
}
