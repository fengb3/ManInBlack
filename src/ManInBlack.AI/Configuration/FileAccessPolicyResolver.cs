using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 从当前 workspace 与配置派生 <see cref="FileAccessPolicy"/>。
/// 不做位置派生:WorkingDirectory 是什么就是什么。
/// ReadableRoots = 配置的显式只读根 + 系统只读根 <c>{RootPath}/skills</c>
/// (SkillService 在宿主侧加载/部署 skills,但 agent 经 RunBash 跑 skill 脚本、经 Read 读 skill 文件时,
/// 该目录需在沙箱内可见)。skills 目录由 <see cref="Services.SkillService"/> 构造时创建,先于任何沙箱内命令执行。
/// </summary>
public sealed class FileAccessPolicyResolver(
    IUserWorkspace workspace,
    IOptions<ManInBlackSettings> settings,
    IOptions<AgentStorageOptions> storageOptions)
{
    public FileAccessPolicy Resolve()
    {
        var roots = (settings.Value.Storage?.FileIsolation?.ReadableRoots ?? [])
            .Select(FileAccessPolicy.Canonicalize)
            .ToList();

        // 系统只读根:{RootPath}/skills。随 RootPath 走(跨平台,与 workspace 位置无关);只读
        // (默认 skills 的部署是宿主侧 SkillService 直接写,不经沙箱)。
        roots.Add(FileAccessPolicy.Canonicalize(
            Path.Combine(storageOptions.Value.RootPath, "skills")));

        return new FileAccessPolicy
        {
            Workspace = FileAccessPolicy.Canonicalize(workspace.WorkingDirectory),
            Temp = FileAccessPolicy.Canonicalize(Path.GetTempPath()),
            ReadableRoots = roots,
            InjectedEnv = settings.Value.Storage?.FileIsolation?.InjectedEnv
                ?? new Dictionary<string, string>()
        };
    }
}
