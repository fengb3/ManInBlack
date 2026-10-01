using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tests.Integration;

/// <summary>
/// 集成测试用的假 <see cref="IChatClient"/>，按预定剧本多轮返回流式响应。
/// 不支持并发调用，每轮按顺序消费 <paramref name="rounds"/> 中的一组更新。
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly IReadOnlyList<IReadOnlyList<ChatResponseUpdate>> _rounds;
    private int _roundIndex;

    public FakeChatClient(IReadOnlyList<IReadOnlyList<ChatResponseUpdate>> rounds)
    {
        _rounds = rounds;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var round = GetRound();
        var assistantMessage = new ChatMessage(ChatRole.Assistant, []);
        foreach (var update in round)
        foreach (var content in update.Contents)
            assistantMessage.Contents.Add(content);

        return Task.FromResult(new ChatResponse(assistantMessage));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var round = GetRound();
        return round.ToAsyncEnumerable();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private IReadOnlyList<ChatResponseUpdate> GetRound()
    {
        var index = _roundIndex++;
        if (index >= _rounds.Count)
            throw new InvalidOperationException($"FakeChatClient 剧本只有 {_rounds.Count} 轮，第 {index + 1} 轮调用超出范围。");
        return _rounds[index];
    }
}
