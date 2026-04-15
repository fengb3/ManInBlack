// using System.Diagnostics;
// using System.Text;
// using ManInBlack.AI.Attributes;
//
// namespace Playground.Tools;
//
// [ServiceRegister.Scoped]
// public partial class CommandLineTools
// {
//     /// <summary>
//     /// run powershell command.
//     /// </summary>
//     /// <param name="command">the command</param>
//     /// <returns>the output of executed command</returns>
//     [AiTool]
//     [AiTool.HasFilter<LoggingToolCallFilter, LongResultReferenceCallFilter>]
//     public string RunPowershell(string command)
//     {
//         var processInfo = new ProcessStartInfo
//         {
//             FileName               = @"pwsh",
//             RedirectStandardOutput = true,
//             RedirectStandardError  = true,
//             StandardOutputEncoding = Encoding.UTF8,
//             StandardErrorEncoding  = Encoding.UTF8,
//             UseShellExecute        = false,
//             CreateNoWindow         = true,
//             // WorkingDirectory       = WorkingDir,
//         };
//         processInfo.ArgumentList.Add("-Command");
//         processInfo.ArgumentList.Add(command);
//         using var process = Process.Start(processInfo);
//         if (process == null)
//             return "Failed to start PowerShell process.";
//
//         var output = process.StandardOutput.ReadToEnd();
//         var error  = process.StandardError.ReadToEnd();
//         process.WaitForExit();
//
//         var result = !string.IsNullOrEmpty(error)
//             ? $"PowerShell error: {error.Trim()}"
//             : output.Trim();
//
//         return result;
//     }
//     
//     /// <summary>
//     /// Run Bash command 
//     /// </summary>
//     /// <param name="command">the command u want to run</param>
//     /// <returns></returns>
//     [AiTool]
//     [AiTool.HasFilter<LoggingToolCallFilter, LongResultReferenceCallFilter>]
//     public string RunBash(string command)
//     {
//         var processInfo = new ProcessStartInfo
//         {
//             FileName               = "/bin/bash",
//             RedirectStandardOutput = true,
//             RedirectStandardError  = true,
//             StandardOutputEncoding = Encoding.UTF8,
//             StandardErrorEncoding  = Encoding.UTF8,
//             UseShellExecute        = false,
//             CreateNoWindow         = true,
//             // WorkingDirectory       = WorkingDir,
//         };
//         processInfo.ArgumentList.Add("-c");
//         processInfo.ArgumentList.Add(command);
//         using var process = Process.Start(processInfo);
//         if (process == null)
//             return "Failed to start Bash process.";
//
//         var output = process.StandardOutput.ReadToEnd();
//         var error  = process.StandardError.ReadToEnd();
//         process.WaitForExit();
//
//         var result = !string.IsNullOrEmpty(error)
//             ? $"Bash error: {error.Trim()}"
//             : output.Trim();
//
//         return result;
//     }
// }