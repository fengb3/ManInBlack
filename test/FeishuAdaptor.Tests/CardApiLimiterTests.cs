using FeishuAdaptor.FeishuCard;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// CardApiLimiter 单元测试 — 验证限流器属性的独立性和完整性。
/// </summary>
public class CardApiLimiterTests
{
    private readonly CardApiLimiter _sut = new();

    #region 属性数量

    [Fact]
    public void CardApiLimiter_应有10个限流器属性()
    {
        // 通过反射确认公开的 SlidingWindowRateLimiter 属性数量
        var properties = typeof(CardApiLimiter)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(SlidingWindowRateLimiter))
            .ToList();

        Assert.Equal(10, properties.Count);
    }

    #endregion

    #region 属性独立性

    [Fact]
    public void 各属性应是独立的SlidingWindowRateLimiter实例()
    {
        var limiters = GetAllLimiters(_sut);

        // 所有实例应互不相同
        var distinctCount = limiters.Distinct().Count();
        Assert.Equal(limiters.Count, distinctCount);
    }

    [Fact]
    public async Task 各限流器应独立计数()
    {
        // 对 CreateCard 消耗配额，不影响其他限流器
        using var cts = new CancellationTokenSource(5000);

        await _sut.CreateCard.WaitForSlotAsync(cts.Token);

        // 其他限流器应仍可调用（不受影响）
        await _sut.SendMessage.WaitForSlotAsync(cts.Token);
        await _sut.StreamingUpdateText.WaitForSlotAsync(cts.Token);
        await _sut.ReplaceElement.WaitForSlotAsync(cts.Token);
    }

    #endregion

    #region 属性名称验证

    [Theory]
    [InlineData(nameof(CardApiLimiter.CreateCard))]
    [InlineData(nameof(CardApiLimiter.SendMessage))]
    [InlineData(nameof(CardApiLimiter.StreamingUpdateText))]
    [InlineData(nameof(CardApiLimiter.ReplaceElement))]
    [InlineData(nameof(CardApiLimiter.FullUpdate))]
    [InlineData(nameof(CardApiLimiter.BatchUpdate))]
    [InlineData(nameof(CardApiLimiter.AddElements))]
    [InlineData(nameof(CardApiLimiter.PartialUpdateElement))]
    [InlineData(nameof(CardApiLimiter.DeleteElement))]
    [InlineData(nameof(CardApiLimiter.UpdateSettings))]
    public void 应包含指定限流器属性(string propertyName)
    {
        var prop = typeof(CardApiLimiter).GetProperty(propertyName);
        Assert.NotNull(prop);
        Assert.Equal(typeof(SlidingWindowRateLimiter), prop.PropertyType);
    }

    #endregion

    #region 辅助方法

    private static List<SlidingWindowRateLimiter> GetAllLimiters(CardApiLimiter limiter)
    {
        return typeof(CardApiLimiter)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(SlidingWindowRateLimiter))
            .Select(p => (SlidingWindowRateLimiter)p.GetValue(limiter)!)
            .ToList();
    }

    #endregion
}
