using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.ToolCallFilters;

namespace ManInBlack.AI.Tools;

/// <summary>
/// 命令行工具，允许 AI 执行系统命令
/// </summary>
[ServiceRegister.Scoped]
public partial class CommandLineTools(IUserWorkspace workspace)
{
    private static readonly ConcurrentDictionary<int, BackgroundTask> BackgroundTasks = new();

    private sealed record BackgroundTask(Process Process, TaskCompletionSource<string> Tcs);

    // /// <summary>
    // /// Run a PowerShell command and return its output.
    // /// </summary>
    // /// <param name="command">The PowerShell command to execute</param>
    // /// <returns>The output of the executed command</returns>
    // [AiTool]
    // [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    // public string RunPowershell(string command)
    // {
    //     Directory.CreateDirectory(workspace.WorkingDirectory);
    //     var processInfo = new ProcessStartInfo
    //     {
    //         FileName               = "pwsh",
    //         WorkingDirectory       = workspace.WorkingDirectory,
    //         RedirectStandardOutput = true,
    //         RedirectStandardError  = true,
    //         StandardOutputEncoding = Encoding.UTF8,
    //         StandardErrorEncoding  = Encoding.UTF8,
    //         UseShellExecute        = false,
    //         CreateNoWindow         = true,
    //     };
    //     processInfo.ArgumentList.Add("-Command");
    //     processInfo.ArgumentList.Add($"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {command}");
    //     using var process = Process.Start(processInfo);
    //     if (process == null)
    //         return "Failed to start PowerShell process.";
    //
    //     var output = process.StandardOutput.ReadToEnd();
    //     var error  = process.StandardError.ReadToEnd();
    //     process.WaitForExit();
    //
    //     return !string.IsNullOrEmpty(error)
    //         ? $"PowerShell error: {error.Trim()}"
    //         : output.Trim();
    // }

    /// <summary>
    /// ---
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
    /// - You can use the run_in_background parameter to run the command in the background.
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
        Directory.CreateDirectory(workspace.WorkingDirectory);
        var processInfo = new ProcessStartInfo
        {
            FileName               = "bash",
            WorkingDirectory       = workspace.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add(command);
        var process = Process.Start(processInfo);
        if (process == null)
            return "Failed to start Bash process.";

        if (runInBackground)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                var output = process.StandardOutput.ReadToEnd();
                var error  = process.StandardError.ReadToEnd();
                var result = !string.IsNullOrEmpty(error)
                    ? $"Bash error: {error.Trim()}"
                    : output.Trim();
                tcs.SetResult(result);
                process.Dispose();
            };
            BackgroundTasks[process.Id] = new BackgroundTask(process, tcs);
            return $"Background task started with ID: {process.Id}. Use GetBackgroundTaskResult to check status.";
        }

        var output = process.StandardOutput.ReadToEnd();
        var error  = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromMilliseconds(timeoutMs)))
        {
            process.Kill();
            process.WaitForExit();
            return $"Bash command timed out after {timeoutMs}ms.";
        }

        process.Dispose();
        return !string.IsNullOrEmpty(error)
            ? $"Bash error: {error.Trim()}"
            : output.Trim();
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
}
