using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.ToolCallFilters;

namespace ManInBlack.AI.Tools;

/// <summary>
/// 命令行工具，允许 AI 执行系统命令
/// </summary>
[ServiceRegister.Scoped]
public partial class CommandLineTools(IUserWorkspace workspace, IShellExecutor shellExecutor)
{
    private static readonly ConcurrentDictionary<int, BackgroundTask> BackgroundTasks = new();

    private sealed record BackgroundTask(Process Process, TaskCompletionSource<string> Tcs);

    /// <summary>
    /// Executes a given bash command and returns its output.
    /// The working directory persists between commands, but shell state does not.
    /// The shell environment is initialized from the user's profile (bash or zsh).
    ///
    /// **IMPORTANT**: Avoid using this tool to run find, grep, cat, head, tail, sed, awk, or echo commands,
    /// unless explicitly instructed or after you have verified that a dedicated tool cannot accomplish your task.
    /// Instead, use the appropriate dedicated tool as this will provide a much better experience for the user:
    ///
    /// - File search: Use Glob (NOT find or ls)
    /// - Content search: Use Grep (NOT grep or rg)
    /// - Read files: Use Read (NOT cat/head/tail)
    /// - Edit files: Use Edit (NOT sed/awk)
    /// - Write files: Use Write
    /// - Communication: Output text directly (NOT echo/printf)
    ///
    /// While the Bash tool can do similar things, it's better to use the built-in
    /// tools as they provide a better user experience and make it easier to
    /// review tool calls and give permission.
    ///
    /// Instructions:
    ///
    /// - If your command will create new directories or files, first use this
    /// tool to run ls to verify the parent directory exists and is the correct
    /// location.
    /// - Always quote file paths that contain spaces with double quotes in your command
    /// (e.g., cd "path with spaces/file.txt")
    /// - Try to maintain your current working directory throughout the session by using absolute paths
    /// and avoiding usage of cd. You may use cd if the User explicitly requests it.
    /// - You may specify an optional timeout in milliseconds (up to 600000ms / 10 minutes).
    /// By default, your command will timeout after 120000ms (2 minutes).
    /// - You can use the runInBackground parameter to run the command in the background.
    /// Only use this if you don't need the result immediately and are OK being notified
    /// when the command finishes later. You do not need to check the output right away -
    /// you will be notified when it finishes.
    ///
    /// When issuing multiple commands:
    /// - If the commands are independent and can run in parallel, make multiple Bash tool calls
    /// in a single message. Example: if you need to run "git status" and "git diff",
    /// send a single message with two Bash tool calls in parallel.
    /// - If the commands depend on each other and must run sequentially, use a single Bash call
    /// with &amp;&amp; to chain them together.
    /// - Only use ; when you need to run commands sequentially but don't care if earlier commands fail.
    /// - DO NOT use newlines to separate commands (newlines are ok in quoted strings).
    /// - Avoid unnecessary sleep commands:
    ///   - Do not sleep between commands that can run immediately - just run them.
    ///   - If you must poll an external process, use GetBackgroundTaskResult to check status
    /// rather than sleeping first.
    ///   - If you must sleep, keep the duration short (1-5 seconds) to avoid blocking the user.
    /// </summary>
    /// <param name="command">The Bash command to execute</param>
    /// <param name="timeoutMs">command execution timeout in milliseconds (default to 120000)</param>
    /// <param name="runInBackground">Run the command in the background and return immediately</param>
    /// <returns>The output of the executed command, or a background task ID</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string RunBash(string command, int timeoutMs = 120000, bool runInBackground = false)
    {
        var dangerCheck = CheckDangerousCommand(command);
        if (dangerCheck != null)
            return dangerCheck;

        if (runInBackground)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.Run(() =>
            {
                try
                {
                    var result = shellExecutor.Execute(command, workspace.WorkingDirectory, timeoutMs);
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
            var taskId = Random.Shared.Next(1, int.MaxValue);
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
    /// Check the result of a background task started by RunBash with runInBackground=true.
    /// </summary>
    /// <param name="taskId">The background task ID returned by RunBash</param>
    /// <returns>The task output if completed, or running status</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
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
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
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
        catch (Exception ex)
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
            return
                "Command blocked by security policy: detected dangerous operation「recursive delete root or home directory」.";

        // Format filesystem
        if (FormatFileSystemRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「format filesystem」.";

        // dd overwrite block device
        if (DdOverwriteBlockDeviceRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「dd overwrite block device」.";

        // Fork bomb
        if (ForkBombRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「fork bomb」.";

        // Shutdown / reboot
        if (ShutdownRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「shutdown/reboot」.";

        // Pipe remote script to shell
        if (PipeRemoteScriptRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「pipe remote script to shell」.";

        // Redirect overwrite block device
        if (RedirectOverwriteBlockDeviceRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「redirect overwrite block device」.";

        // Flush firewall rules
        if (FlushFirewallRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「flush firewall rules」.";

        // Reverse shell / network listener
        if (ReverseShellNetworkListener().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「reverse shell / network listener」.";

        // Overwrite critical system files
        if (OverwriteCriticalSystemFilesRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「overwrite critical system files」.";

        // Create or modify Linux user
        if (CreateLinuxUserRegex().IsMatch(command))
            return
                "Command blocked by security policy: detected dangerous operation「create or modify Linux user」.";

        return null;
    }

    /// <summary>
    /// 匹配递归强制删除根目录或家目录的命令，如 <c>rm -rf /</c>、<c>rm -rf /*</c>、<c>rm --force ~</c>、<c>rm -rf $HOME</c>。
    /// </summary>
    [GeneratedRegex(@"rm\s+(?:-[a-zA-Z]*f[a-zA-Z]*\s+|--force\s+)(?:/\s*$|/\*|~|\$HOME)", RegexOptions.IgnoreCase,
        "zh-CN")]
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
    [GeneratedRegex(@"\b(shutdown|reboot|poweroff|halt|init\s+[06])\b", RegexOptions.IgnoreCase, "zh-CN")]
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