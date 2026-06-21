using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI;

/// <summary>
/// 流式配置 builder。外部程序集（如 FeishuAdaptor）可通过 <see cref="Services"/> 挂自己的扩展方法。
/// </summary>
public interface IManInBlackBuilder
{
    IServiceCollection Services { get; }
}
