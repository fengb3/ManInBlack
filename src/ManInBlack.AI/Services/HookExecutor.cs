using System.Text.Json;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

/// <summary>
/// 钩子执行引擎，负责加载全局与用户级钩子配置、按挂载点和工具名匹配、
/// 依次执行脚本并合并结果。第一个返回 IsBlocked=true 的钩子会中断后续执行。
/// </summary>
[ServiceRegister.Scoped.As<IHookExecutor>]
public class HookExecutor(
    IOptions<ManInBlackSettings> settings,
    IOptions<AgentStorageOptions> storageOptions,
    IShellExecutor shellExecutor,
    IUserWorkspace userWorkspace,
    ILogger<HookExecutor> logger) : IHookExecutor
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>懒加载缓存，每个 Scope 只加载一次</summary>
    private List<(HookSettings Hook, bool IsGlobal)>? _cachedHooks;

    /// <inheritdoc />
    /// 执行指定挂载点的所有已启用钩子（全局先于用户），返回合并后的结果。
    /// 第一个返回 IsBlocked=true 的钩子会短路，不再执行后续钩子。
    /// 多个钩子的 InjectedText 会被拼接为单个字符串。
    /// </summary>
    /// <param name="point">目标挂载点</param>
    /// <param name="context">传递给钩子脚本的上下文数据</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的钩子执行结果</returns>
    public Task<HookResult> ExecuteAsync(HookPoint point, HookContext context, CancellationToken ct = default)
    {
        var allHooks = _cachedHooks ??= LoadAllHooks();

        logger.LogDebug("[Hook] 执行挂载点 {HookPoint}，已加载 {Total} 个钩子（全局 {Global}，用户 {User}），工作空间：{Workspace}",
            point, allHooks.Count,
            allHooks.Count(h => h.IsGlobal), allHooks.Count(h => !h.IsGlobal),
            userWorkspace.WorkingDirectory);

        // 按 HookPoint + ToolNames + Enabled 过滤
        var matched = allHooks
            .Where(h => h.Hook.Enabled
                && Enum.TryParse<HookPoint>(h.Hook.HookPoint, out var hp) && hp == point
                && IsToolNameMatch(h.Hook, context))
            .ToList();

        if (matched.Count == 0)
        {
            logger.LogDebug("[Hook] 挂载点 {HookPoint} 无匹配钩子，跳过", point);
            return Task.FromResult(new HookResult { Succeeded = true });
        }

        logger.LogDebug("[Hook] 挂载点 {HookPoint} 匹配 {Count} 个钩子：{Names}",
            point, matched.Count, string.Join(", ", matched.Select(h => h.Hook.Name)));

        var injectedTexts = new List<string>();
        string? injectTarget = null;

        foreach (var (hook, isGlobal) in matched)
        {
            ct.ThrowIfCancellationRequested();

            var scriptCommand = ResolveScriptCommand(hook, isGlobal, out var workingDir);
            logger.LogDebug("[Hook] 执行钩子 {Name}，命令：{Command}，工作目录：{WorkingDir}",
                hook.Name, scriptCommand, workingDir);

            var result = ExecuteSingleScript(scriptCommand, workingDir, hook, context);

            logger.LogDebug("[Hook] 钩子 {Name} 返回：IsBlocked={IsBlocked}, InjectedText={HasText}, Succeeded={Succeeded}",
                hook.Name, result.IsBlocked, !string.IsNullOrEmpty(result.InjectedText), result.Succeeded);

            // 第一个 IsBlocked 短路
            if (result.IsBlocked)
            {
                logger.LogInformation("[Hook] 挂载点 {HookPoint} 被钩子 {Name} 阻断：{Reason}",
                    point, hook.Name, result.BlockReason);

                return Task.FromResult(new HookResult
                {
                    IsBlocked = true,
                    BlockReason = result.BlockReason,
                    InjectedText = injectedTexts.Count > 0
                        ? string.Join(Environment.NewLine, injectedTexts)
                        : null,
                    InjectTarget = injectTarget,
                    Succeeded = true,
                });
            }

            if (!string.IsNullOrEmpty(result.InjectedText))
                injectedTexts.Add(result.InjectedText);

            if (result.InjectTarget is not null)
                injectTarget = result.InjectTarget;
        }

        return Task.FromResult(new HookResult
        {
            IsBlocked = false,
            InjectedText = injectedTexts.Count > 0
                ? string.Join(Environment.NewLine, injectedTexts)
                : null,
            InjectTarget = injectTarget,
            Succeeded = true,
        });
    }

    /// <summary>
    /// 加载全部钩子：全局钩子（来自 ManInBlackSettings.Hooks）+ 用户钩子（来自工作空间 .agents/mib-hooks.json）
    /// </summary>
    private List<(HookSettings Hook, bool IsGlobal)> LoadAllHooks()
    {
        var result = new List<(HookSettings, bool)>();

        // 全局钩子
        foreach (var hook in settings.Value.Hooks)
            result.Add((hook, true));

        // 用户钩子：从 {workspace}/.agents/mib-hooks.json 读取
        var userHooksPath = Path.Combine(userWorkspace.WorkingDirectory, ".agents", "mib-hooks.json");
        logger.LogDebug("[Hook] 用户钩子配置路径：{Path}，存在：{Exists}", userHooksPath, File.Exists(userHooksPath));
        if (File.Exists(userHooksPath))
        {
            try
            {
                var json = File.ReadAllText(userHooksPath);
                var userHooks = JsonSerializer.Deserialize<List<HookSettings>>(json, JsonReadOptions);
                if (userHooks is not null)
                {
                    foreach (var hook in userHooks)
                        result.Add((hook, false));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "读取用户钩子配置失败：{Path}", userHooksPath);
            }
        }

        return result;
    }

    /// <summary>
    /// 检查钩子的 ToolNames 过滤条件：为空则匹配所有工具，否则要求 context.ToolName 在列表中
    /// </summary>
    private static bool IsToolNameMatch(HookSettings hook, HookContext context)
    {
        if (hook.ToolNames.Count == 0)
            return true;

        return !string.IsNullOrEmpty(context.ToolName) && hook.ToolNames.Contains(context.ToolName);
    }

    /// <summary>
    /// 解析脚本命令：全局钩子工作目录为 {RootPath}/hooks/，用户钩子工作目录为 {workspace}/。
    /// Script 字段直接作为 shell 命令使用（如 "python security_check.py"），不再拼接路径前缀。
    /// </summary>
    private string ResolveScriptCommand(HookSettings hook, bool isGlobal, out string workingDir)
    {
        if (isGlobal)
        {
            workingDir = Path.Combine(storageOptions.Value.RootPath, "hooks");
        }
        else
        {
            workingDir = userWorkspace.WorkingDirectory;
        }

        return hook.Script;
    }

    /// <summary>
    /// 执行单个钩子脚本：将 HookContext 序列化为 JSON 经 stdin 传入脚本（写完即关闭 stdin 发 EOF），
    /// 从 stdout 解析 HookResult。异常会被捕获并记录，不会向上传播。
    /// </summary>
    private HookResult ExecuteSingleScript(string scriptCommand, string workingDir, HookSettings hook, HookContext context)
    {
        try
        {
            // 序列化上下文为 JSON，经 stdin 传入脚本。stdin 是进程 fd，不依赖任何文件系统挂载，
            // 沙盒隔离对它透明（取代旧方案「序列化到临时文件落在 workingDir」的 /tmp 可见性 workaround）。
            var contextJson = JsonSerializer.Serialize(context, JsonWriteOptions);

            var shellResult = shellExecutor.Execute(scriptCommand, workingDir, hook.TimeoutMs, stdin: contextJson);

            // 空 stdout 视为无操作
            if (string.IsNullOrWhiteSpace(shellResult.StandardOutput))
                return new HookResult { Succeeded = true };

            // 从 stdout 解析 HookResult
            var hookResult = JsonSerializer.Deserialize<HookResult>(
                shellResult.StandardOutput, JsonReadOptions);

            return hookResult ?? new HookResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "钩子脚本执行异常：{Name} ({Script})", hook.Name, hook.Script);
            return new HookResult { Succeeded = false, ErrorMessage = ex.Message };
        }
    }
}
