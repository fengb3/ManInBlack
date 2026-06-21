using ManInBlack.AI;
using ManInBlack.AI.Configuration;

namespace FeishuAdaptor;

/// <summary>
/// 在核心 builder 之上挂飞书配置（核心库不感知适配器概念）。
/// </summary>
public static class FeishuBuilderExtensions
{
    /// <summary>
    /// 向 <see cref="IManInBlackBuilder"/> 添加飞书配置。
    /// </summary>
    /// <param name="builder">ManInBlack 核心构建器。</param>
    /// <param name="configure">飞书配置委托。</param>
    /// <returns>同一条构建器链，支持流式调用。</returns>
    public static IManInBlackBuilder AddFeishu(this IManInBlackBuilder builder, Action<FeishuSettings> configure)
    {
        builder.Services.Configure(configure);
        return builder;
    }
}
