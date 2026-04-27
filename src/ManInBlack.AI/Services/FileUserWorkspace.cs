using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于文件系统的用户工作空间实现
/// </summary>
[ServiceRegister.Scoped.As<IUserWorkspace>]
public class FileUserWorkspace(IOptions<AgentStorageOptions> options, AgentContext agentContext, IUserStorage userStorage) : IUserWorkspace
{
    private readonly UserEntry _user = userStorage.GetOrCreateUser(agentContext.ParentId).GetAwaiter().GetResult();

    /// <inheritdoc />
    public string WorkingDirectory
    {
        get
        {
            var path = Path.Combine(options.Value.RootPath, "workspaces", $"{_user.SelfHostUserId}");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }
    }
}
