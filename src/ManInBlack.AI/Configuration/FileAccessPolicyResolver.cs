using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 从当前 workspace 与配置派生 <see cref="FileAccessPolicy"/>。
/// 不做位置派生:WorkingDirectory 是什么就是什么;ReadableRoots 全部来自配置。
/// </summary>
public sealed class FileAccessPolicyResolver(
    IUserWorkspace workspace,
    IOptions<ManInBlackSettings> settings)
{
    public FileAccessPolicy Resolve()
    {
        var roots = (settings.Value.Storage?.FileIsolation?.ReadableRoots ?? [])
            .Select(FileAccessPolicy.Canonicalize)
            .ToList();

        return new FileAccessPolicy
        {
            Workspace = FileAccessPolicy.Canonicalize(workspace.WorkingDirectory),
            Temp = FileAccessPolicy.Canonicalize(Path.GetTempPath()),
            ReadableRoots = roots
        };
    }
}
