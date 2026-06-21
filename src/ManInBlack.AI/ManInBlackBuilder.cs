using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI;

/// <summary>
/// IManInBlackBuilder 的默认实现。同程序集内的流式扩展方法通过强转访问 internal <see cref="AddContribution"/>。
/// </summary>
internal sealed class ManInBlackBuilder(IServiceCollection services) : IManInBlackBuilder
{
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// 注册一条配置贡献（IOptions 首次 resolve 时按序合并）。
    /// </summary>
    internal void AddContribution(IManInBlackContribution contribution)
        => Services.AddSingleton<IManInBlackContribution>(contribution);
}
