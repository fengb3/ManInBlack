namespace ManInBlack.AI.Abstraction.Hooks;

/// <summary>
/// 钩子执行器，负责加载全局/用户配置、匹配节点、运行脚本并返回结果
/// </summary>
public interface IHookExecutor
{
    /// <summary>
    /// 执行指定挂载点的所有钩子（全局先执行，用户后执行），返回合并后的结果
    /// </summary>
    Task<HookResult> ExecuteAsync(HookPoint point, HookContext context, CancellationToken ct = default);
}
