using System.Diagnostics;
using System.Text;
using ManInBlack.AI.Attributes;
using ManInBlack.AI.Tools;

namespace AgentConsole.Tools;

/// <summary>
/// 命令行工具，允许 AI 执行系统命令
/// </summary>
[ServiceRegister.Scoped]
public partial class CommandLineTools
{
    /// <summary>
    /// Run a PowerShell command and return its output.
    /// </summary>
    /// <param name="command">The PowerShell command to execute</param>
    /// <returns>The output of the executed command</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter>]
    public string RunPowershell(string command)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName               = "pwsh",
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
    public string RunBash(string command)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName               = "/bin/bash",
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

[ServiceRegister.Scoped]
public class LoggingFilter : ToolCallFilter
{
    public override async Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
    {
        var arguments = context.Arguments.Select(pair => $"{pair.Key}: {pair.Value}").ToArray();
        
        // set console color for better visibility
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine();
        Console.WriteLine($"[ToolCall] {context.ToolName} ({string.Join(",", arguments)})");
        await next(context);
        var result = context.Result;
        Console.WriteLine($"[ToolResult] {context.ToolName} => {result}");
        Console.ForegroundColor = originalColor;
    }
}