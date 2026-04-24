using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Tests.Helpers;

public static class TestHelpers
{
    /// <summary>
    /// 空 IServiceProvider，用于不需要依赖注入的中间件测试
    /// </summary>
    public static readonly IServiceProvider EmptyServiceProvider =
        new ServiceCollection().BuildServiceProvider();

    /// <summary>
    /// 空的 IAsyncEnumerable，用于不需要响应流的 next delegate
    /// </summary>
    public static IAsyncEnumerable<ChatResponseUpdate> EmptyStream =>
        AsyncEnumerable.Empty<ChatResponseUpdate>();

    /// <summary>
    /// 将零个或多个元素包装为 IAsyncEnumerable
    /// </summary>
    public static IAsyncEnumerable<T> AsyncSeq<T>(params T[] items) =>
        items.ToAsyncEnumerable();

    /// <summary>
    /// 返回一个 IAsyncEnumerable，在 MoveNextAsync 时抛出指定异常
    /// 用于测试重试逻辑
    /// </summary>
    public static async IAsyncEnumerable<T> ThrowOnMoveNext<T>(Exception ex)
    {
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    /// <summary>
    /// 提取所有 ChatResponseUpdate 中的 TextContent
    /// </summary>
    public static IEnumerable<string> ExtractTexts(this IReadOnlyCollection<ChatResponseUpdate> updates)
    {
        return updates.SelectMany(u => u.Contents.OfType<TextContent>()).Select(t => t.Text);
    }
}
