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
    /// <summary>
    /// Run a PowerShell command and return its output.
    /// </summary>
    /// <param name="command">The PowerShell command to execute</param>
    /// <returns>The output of the executed command</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string RunPowershell(string command)
    {
        Directory.CreateDirectory(workspace.WorkingDirectory);
        var processInfo = new ProcessStartInfo
        {
            FileName               = "pwsh",
            WorkingDirectory       = workspace.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        processInfo.ArgumentList.Add("-Command");
        processInfo.ArgumentList.Add($"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {command}");
        using var process = Process.Start(processInfo);
        if (process == null)
            return "Failed to start PowerShell process.";

        var output = process.StandardOutput.ReadToEnd();
        var error  = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return !string.IsNullOrEmpty(error)
            ? $"PowerShell error: {error.Trim()}"
            : output.Trim();
    }

    /// <summary>
    /// Run a Bash command and return its output.
    /// </summary>
    /// <param name="command">The Bash command to execute</param>
    /// <returns>The output of the executed command</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string RunBash(string command)
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
        using var process = Process.Start(processInfo);
        if (process == null)
            return "Failed to start Bash process.";

        var output = process.StandardOutput.ReadToEnd();
        var error  = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return !string.IsNullOrEmpty(error)
            ? $"Bash error: {error.Trim()}"
            : output.Trim();
    }
}