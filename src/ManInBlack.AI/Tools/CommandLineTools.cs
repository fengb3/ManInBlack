using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.ToolCallFilters;

namespace ManInBlack.AI.Tools;

/// <summary>
/// 命令行工具，允许 AI 执行系统命令
/// </summary>
[ServiceRegister.Scoped]
public partial class CommandLineTools(IUserWorkspace workspace, IShellExecutor shellExecutor)
{
    /// <summary>
    /// 存储后台任务的字典，键为任务 ID，值为包含 Process 和 TaskCompletionSource 的 BackgroundTask 记录。
    /// </summary>
    private static readonly ConcurrentDictionary<int, BackgroundTask> BackgroundTasks = new();

    /// <summary>
    /// 用于生成后台任务 ID 的原子计数器，初始值为 0，每次创建新任务时递增。
    /// </summary>
    private static int _nextTaskId;

    /// <summary>
    /// BackgroundTask 记录类型，包含一个可选的 <c>Process</c> 对象（如果需要终止进程）和一个 <c>TaskCompletionSource&lt;string&gt;</c> 用于存储命令输出结果。
    /// </summary>
    /// <param name="Process"></param>
    /// <param name="Tcs"></param>
    private sealed record BackgroundTask(Process? Process, TaskCompletionSource<string> Tcs);

    /// <summary>
    /// 执行给定的 Bash 命令并返回输出。工作目录在命令之间保持不变，但 Shell 状态不会。
    /// Shell 环境从用户的配置文件（bash 或 zsh）初始化。
    ///
    /// **可操作目录**：工作空间目录和 /tmp 是可读写的。注意 /tmp 的内容在命令之间会被清空，
    /// 如需跨命令保留临时文件，请使用工作空间目录。
    ///
    /// **重要提示**：除非明确指示，或在确认专用工具无法完成任务后，请勿使用此工具运行
    /// find、grep、cat、head、tail、sed、awk 或 echo 命令。请使用相应的专用工具，
    /// 这将为用户提供更好的体验：
    ///
    /// - 文件搜索：使用 Glob（而非 find 或 ls）
    /// - 内容搜索：使用 Grep（而非 grep 或 rg）
    /// - 读取文件：使用 Read（而非 cat/head/tail）
    /// - 编辑文件：使用 Edit（而非 sed/awk）
    /// - 写入文件：使用 Write
    /// - 通信输出：直接输出文本（而非 echo/printf）
    ///
    /// 使用说明：
    ///
    /// - 命令的第一行必须是以 # 开头的单行注释，用一句话说明命令的作用。
    /// - 注释行之后，在下一行放置实际命令。
    /// - 如果命令会创建新目录或文件，请先使用此工具运行 ls 验证父目录存在且位置正确。
    /// - 始终用双引号包裹包含空格的文件路径（如 cd "path with spaces/file.txt"）。
    /// - 尽量在会话中使用绝对路径维持当前工作目录，避免使用 cd。用户明确要求时可使用 cd。
    /// - 可以指定可选的超时时间（毫秒），最长 600000ms（10 分钟）。默认超时 120000ms（2 分钟）。
    /// - 可以使用 runInBackground 参数在后台运行命令。仅在不需要立即获取结果时使用，
    ///   命令完成后会收到通知，无需立即检查输出。
    ///
    /// 多命令执行：
    /// - 如果命令相互独立且可并行执行，在一条消息中发起多个 Bash 工具调用。
    ///   例如：需要运行 "git status" 和 "git diff"，在一条消息中并行发送两个 Bash 调用。
    /// - 如果命令相互依赖必须顺序执行，使用单个 Bash 调用并用 &amp;&amp; 链接。
    /// - 仅在不关心前一个命令是否失败时使用 ; 分隔命令。
    /// - 除必须的首行注释外，不要使用额外的换行分隔命令（引号字符串中的换行除外）。
    /// - 避免不必要的 sleep 命令：
    ///   - 可立即执行的命令之间不要 sleep。
    ///   - 如需轮询外部进程，使用 GetBackgroundTaskResult 检查状态，而非先 sleep。
    ///   - 如必须 sleep，保持短时间（1-5 秒），避免阻塞用户。
    /// </summary>
    /// <param name="command">要执行的 Bash 命令</param>
    /// <param name="timeoutMs">命令执行超时时间（毫秒），默认 120000</param>
    /// <param name="runInBackground">是否在后台运行命令并立即返回</param>
    /// <returns>执行命令的输出，或后台任务 ID</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public string RunBash(string command, int timeoutMs = 120000, bool runInBackground = false)
    {
        var dangerCheck = CheckDangerousCommand(command);
        if (dangerCheck != null)
            return dangerCheck;

        if (runInBackground)
        {
            var tcs = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            Task.Run(() =>
            {
                try
                {
                    var result = shellExecutor.Execute(
                        command,
                        workspace.WorkingDirectory,
                        timeoutMs
                    );
                    var output = !string.IsNullOrEmpty(result.StandardError)
                        ? $"Bash error: {result.StandardError.Trim()}"
                        : result.StandardOutput.Trim();
                    tcs.SetResult(output);
                }
                catch (Exception ex)
                {
                    tcs.SetResult($"Bash error: {ex.Message}");
                }
            });
            // 用哈希生成一个伪 task ID（不再依赖 Process.Id）
            var taskId = Interlocked.Increment(ref _nextTaskId);
            BackgroundTasks[taskId] = new BackgroundTask(null!, tcs);
            return $"Background task started with ID: {taskId}. Use GetBackgroundTaskResult to check status.";
        }

        var shellResult = shellExecutor.Execute(command, workspace.WorkingDirectory, timeoutMs);

        if (shellResult.TimedOut)
            return $"Bash command timed out after {timeoutMs}ms.";

        return !string.IsNullOrEmpty(shellResult.StandardError)
            ? $"Bash error: {shellResult.StandardError.Trim()}"
            : shellResult.StandardOutput.Trim();
    }

    /// <summary>
    /// 检查后台任务的执行结果。仅适用于使用 runInBackground=true 启动的 RunBash 命令。返回任务输出或运行状态。完成后会从后台任务列表中移除该任务。
    /// </summary>
    /// <param name="taskId">后台任务 ID</param>
    /// <returns>任务输出（如果已完成）或运行状态</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public string GetBackgroundTaskResult(int taskId)
    {
        if (!BackgroundTasks.TryGetValue(taskId, out var task))
            return $"No background task found with ID: {taskId}.";

        if (!task.Tcs.Task.IsCompleted)
            return $"Background task {taskId} is still running.";

        BackgroundTasks.TryRemove(taskId, out _);
        return task.Tcs.Task.Result;
    }

    /// <summary>
    /// 终止后台任务。停止关联进程并将结果设为已取消。
    /// </summary>
    /// <param name="taskId">后台任务 ID</param>
    /// <returns>终止结果</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public string KillBackgroundTask(int taskId)
    {
        if (!BackgroundTasks.TryGetValue(taskId, out var task))
            return $"No background task found with ID: {taskId}.";

        BackgroundTasks.TryRemove(taskId, out _);

        try
        {
            if (task.Process is not null && !task.Process.HasExited)
                task.Process.Kill();
        }
        catch (Exception)
        {
            // 进程已退出或无法终止，继续完成 TCS
        }

        task.Tcs.TrySetResult($"Background task {taskId} has been killed.");

        return $"Background task {taskId} has been killed.";
    }

    /// <summary>
    /// check if a command is prohibited
    /// </summary>
    /// <param name="command">command to check</param>
    /// <returns>prohibit message</returns>
    private static string? CheckDangerousCommand(string command)
    {
        // Recursive delete root or home directory
        if (RecursiveDeleteRootOrHomeDirRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「recursive delete root or home directory」.";

        // Format filesystem
        if (FormatFileSystemRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「format filesystem」.";

        // dd overwrite block device
        if (DdOverwriteBlockDeviceRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「dd overwrite block device」.";

        // Fork bomb
        if (ForkBombRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「fork bomb」.";

        // Shutdown / reboot
        if (ShutdownRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「shutdown/reboot」.";

        // Pipe remote script to shell
        if (PipeRemoteScriptRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「pipe remote script to shell」.";

        // Redirect overwrite block device
        if (RedirectOverwriteBlockDeviceRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「redirect overwrite block device」.";

        // Flush firewall rules
        if (FlushFirewallRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「flush firewall rules」.";

        // Reverse shell / network listener
        if (ReverseShellNetworkListener().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「reverse shell / network listener」.";

        // Overwrite critical system files
        if (OverwriteCriticalSystemFilesRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「overwrite critical system files」.";

        // Create or modify Linux user
        if (CreateLinuxUserRegex().IsMatch(command))
            return "Command blocked by security policy: detected dangerous operation「create or modify Linux user」.";

        return null;
    }

    /// <summary>
    /// 匹配递归强制删除根目录或家目录的命令，如 <c>rm -rf /</c>、<c>rm -rf /*</c>、<c>rm --force ~</c>、<c>rm -rf $HOME</c>。
    /// </summary>
    [GeneratedRegex(
        @"rm\s+(?:-[a-zA-Z]*f[a-zA-Z]*\s+|--force\s+)(?:/\s*$|/\*|~|\$HOME)",
        RegexOptions.IgnoreCase,
        "zh-CN"
    )]
    private static partial Regex RecursiveDeleteRootOrHomeDirRegex();

    /// <summary>
    /// 匹配格式化文件系统的命令，如 <c>mkfs.ext4 /dev/sda1</c>。
    /// </summary>
    [GeneratedRegex(@"\bmkfs\b", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex FormatFileSystemRegex();

    /// <summary>
    /// 匹配使用 <c>dd</c> 直接写入块设备的命令，如 <c>dd if=/dev/zero of=/dev/sda</c>。
    /// </summary>
    [GeneratedRegex(@"\bdd\s+.*of=/dev/", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex DdOverwriteBlockDeviceRegex();

    /// <summary>
    /// 匹配 Bash fork 炸弹，如 <c>:(){ :|:&amp; }</c>。
    /// </summary>
    [GeneratedRegex(@":\(\)\{.*:\|:&")]
    private static partial Regex ForkBombRegex();

    /// <summary>
    /// 匹配关机、重启相关命令，如 <c>shutdown</c>、<c>reboot</c>、<c>poweroff</c>、<c>halt</c>、<c>init 0</c>、<c>init 6</c>。
    /// </summary>
    [GeneratedRegex(
        @"\b(shutdown|reboot|poweroff|halt|init\s+[06])\b",
        RegexOptions.IgnoreCase,
        "zh-CN"
    )]
    private static partial Regex ShutdownRegex();

    /// <summary>
    /// 匹配从网络下载并直接执行脚本的管道命令，如 <c>curl http://example.com/script.sh | sh</c>。
    /// </summary>
    [GeneratedRegex(@"(wget|curl)\s+.*\|\s*(ba)?sh", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex PipeRemoteScriptRegex();

    /// <summary>
    /// 匹配通过输出重定向覆盖块设备的命令，如 <c>&gt; /dev/sda</c>。
    /// </summary>
    [GeneratedRegex(@">\s*/dev/[sh]d", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex RedirectOverwriteBlockDeviceRegex();

    /// <summary>
    /// 匹配清空防火墙规则的命令，如 <c>iptables -F</c>。
    /// </summary>
    [GeneratedRegex(@"\biptables\s+-F\b", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex FlushFirewallRegex();

    /// <summary>
    /// 匹配反向 Shell 或网络监听命令，如 <c>nc -l</c>、<c>nc -e</c>、<c>/dev/tcp/</c>。
    /// </summary>
    [GeneratedRegex(@"\bnc\s+.*-[el]\b|/dev/tcp/", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex ReverseShellNetworkListener();

    /// <summary>
    /// 匹配覆写关键系统文件的命令，如 <c>&gt; /etc/passwd</c>、<c>&gt; /etc/shadow</c>、<c>&gt; /etc/sudoers</c>。
    /// </summary>
    [GeneratedRegex(@">\s*/etc/(passwd|shadow|sudoers)\b", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex OverwriteCriticalSystemFilesRegex();

    /// <summary>
    /// 匹配创建或修改 Linux 用户的命令，如 <c>useradd</c>、<c>adduser</c>、<c>passwd</c>。
    /// </summary>
    [GeneratedRegex(@"\b(useradd|adduser|passwd)\b", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex CreateLinuxUserRegex();
}
