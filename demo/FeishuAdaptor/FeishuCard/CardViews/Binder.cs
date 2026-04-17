using FeishuAdaptor.FeishuCard.Cards;

namespace FeishuAdaptor.FeishuCard.CardViews;

/// <summary>
/// 捕获一条 ViewModel 属性 → CardElement 的绑定关系。
/// </summary>
public sealed class Binder<TViewModel>
{
    /// <summary>
    /// ViewModel 上被监听的属性名。
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// 卡片元素的唯一标识，用于 Cardkit API 路由。
    /// </summary>
    public required string ElementId { get; init; }

    /// <summary>
    /// 卡片元素实例（record 引用，属性可变）。
    /// </summary>
    public required CardElement Element { get; init; }

    /// <summary>
    /// 读取 ViewModel 当前值并应用到 Element 的委托。
    /// </summary>
    public required Action<TViewModel> Apply { get; init; }
}
