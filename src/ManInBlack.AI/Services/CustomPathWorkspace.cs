using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

/// <summary>
/// 工作空间使用配置中指定的显式路径
/// </summary>
public class CustomPathWorkspace(IOptions<AgentStorageOptions> options) : IUserWorkspace
{
    public string WorkingDirectory
    {
        get
        {
            var path = options.Value.Workspace.CustomPath
                ?? throw new InvalidOperationException("CustomPath 模式未配置路径");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }
    }
}
