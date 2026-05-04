using ManInBlack.AI.Abstraction.Attributes;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 飞书卡片 API 限流注册表 — 每个 API 接口拥有独立的滑动窗口限流器。
/// <para>限流规则：50 次/秒、1000 次/分钟（各接口独立计算）。</para>
/// </summary>
[ServiceRegister.Singleton]
public sealed class CardApiLimiter
{
    /// <summary>创建卡片实体 — POST /cardkit/v1/cards</summary>
    public SlidingWindowRateLimiter CreateCard { get; } = new(50, 1000);

    /// <summary>发送消息 — POST /im/v1/messages</summary>
    public SlidingWindowRateLimiter SendMessage { get; } = new(50, 1000);

    /// <summary>流式更新文本 — PUT /cardkit/v1/cards/:card_id/elements/:element_id/content</summary>
    public SlidingWindowRateLimiter StreamingUpdateText { get; } = new(50, 1000);

    /// <summary>全量替换组件 — PUT /cardkit/v1/cards/:card_id/elements/:element_id</summary>
    public SlidingWindowRateLimiter ReplaceElement { get; } = new(50, 1000);

    /// <summary>全量更新卡片 — PUT /cardkit/v1/cards/:card_id</summary>
    public SlidingWindowRateLimiter FullUpdate { get; } = new(50, 1000);

    /// <summary>局部更新卡片 — POST /cardkit/v1/cards/:card_id/batch_update</summary>
    public SlidingWindowRateLimiter BatchUpdate { get; } = new(50, 1000);

    /// <summary>新增组件 — POST /cardkit/v1/cards/:card_id/elements</summary>
    public SlidingWindowRateLimiter AddElements { get; } = new(50, 1000);

    /// <summary>更新组件属性 — PATCH /cardkit/v1/cards/:card_id/elements/:element_id</summary>
    public SlidingWindowRateLimiter PartialUpdateElement { get; } = new(50, 1000);

    /// <summary>删除组件 — DELETE /cardkit/v1/cards/:card_id/elements/:element_id</summary>
    public SlidingWindowRateLimiter DeleteElement { get; } = new(50, 1000);

    /// <summary>更新卡片配置 — PATCH /cardkit/v1/cards/:card_id/settings</summary>
    public SlidingWindowRateLimiter UpdateSettings { get; } = new(50, 1000);
}
