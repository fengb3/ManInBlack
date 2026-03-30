using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI;

/// <summary>
/// Anthropic 兼容 API 适配器，实现 IChatClient 接口
/// 可连接任何兼容 Anthropic API 形状的接口
/// </summary>
public sealed class AnthropicCompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly string _endPoint;


    /// <summary>
    /// 创建 Anthropic 兼容聊天客户端
    /// </summary>
    /// <param name="httpClient">HttpClient 实例，通过依赖注入提供</param>
    /// <param name="modelId">模型 ID</param>
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
        var messageList = messages.ToList();

        var requestBody = new
        {
            model = _modelId,
            max_tokens = options?.MaxOutputTokens ?? 4096,
            temperature = (float?)(options?.Temperature ?? 1.0),
            top_p = (float?)(options?.TopP),
            system = messageList.FirstOrDefault(m => m.Role == ChatRole.System)?.Text,
            messages = messageList
                .Where(m => m.Role != ChatRole.System)
                .Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.Text
                })
                .ToArray()
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_endPoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<AnthropicResponse>(responseJson)
            ?? throw new InvalidOperationException($"Failed to parse Anthropic-compatible response with model {_modelId} to url {_httpClient.BaseAddress}");

        var text = result.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? "";

        var chatResponse = new ChatResponse(
            new List<ChatMessage> { new ChatMessage(ChatRole.Assistant, text) }
        );

        if (result.Usage is not null)
        {
#pragma warning disable CS8602
            var usage = result.Usage!;
            chatResponse.AdditionalProperties["Usage"] = new
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.InputTokens + usage.OutputTokens
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
        var messageList = messages.ToList();

        var requestBody = new
        {
            model = _modelId,
            max_tokens = options?.MaxOutputTokens ?? 4096,
            temperature = (float?)(options?.Temperature ?? 1.0),
            top_p = (float?)(options?.TopP),
            stream = true,
            system = messageList.FirstOrDefault(m => m.Role == ChatRole.System)?.Text,
            messages = messageList
                .Where(m => m.Role != ChatRole.System)
                .Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.Text
                })
                .ToArray()
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

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

            AnthropicStreamResponse? result = null;
            try
            {
                result = JsonSerializer.Deserialize<AnthropicStreamResponse>(jsonStr);
            }
            catch { }

            var delta = result?.Delta?.Text;
            if (!string.IsNullOrEmpty(delta))
            {
                var update = new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(delta)]
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

    private class AnthropicResponse
    {
        public List<AnthropicContent>? Content { get; set; }
        public AnthropicUsage? Usage { get; set; }
        public string? StopReason { get; set; }
    }

    private class AnthropicContent
    {
        public string Type { get; set; } = "";
        public string? Text { get; set; }
    }

    private class AnthropicUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    private class AnthropicStreamResponse
    {
        public AnthropicDelta? Delta { get; set; }
    }

    private class AnthropicDelta
    {
        public string? Text { get; set; }
    }

    #endregion
}
