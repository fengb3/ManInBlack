using System.Diagnostics;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Services;

[ServiceRegister.Singleton]
public class GitHubCliSetup(ILogger<GitHubCliSetup> logger)
{
    public async Task LoginAsync(string token, CancellationToken ct = default)
    {
        await RunProcessAsync("auth login --with-token", token, ct);
        logger.LogInformation("gh CLI 认证成功");
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await RunProcessAsync("auth logout", input: null, ct);
            logger.LogInformation("gh CLI 登出成功");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "gh CLI 登出失败，忽略");
        }
    }

    public async Task<string> RunGhAsync(string args, CancellationToken ct = default)
    {
        return await RunProcessAsync(args, input: null, ct);
    }

    private async Task<string> RunProcessAsync(string args, string? input, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "gh",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input != null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        if (input != null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"gh {args} 失败 (exit {process.ExitCode}): {stderr}");
        }

        return stdout;
    }
}