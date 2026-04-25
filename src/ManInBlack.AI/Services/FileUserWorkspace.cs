using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Core.Storage;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

/// <summary>
/// 基于文件系统的用户工作空间实现
/// </summary>
[ServiceRegister.Scoped.As<IUserWorkspace>]
public class FileUserWorkspace : IUserWorkspace
{
    private readonly string _userId;
    private readonly string _rootPath;
    private readonly UserEntry _user;

    public FileUserWorkspace(IOptions<AgentStorageOptions> options, AgentContext agentContext, IUserStorage userStorage)
    {
        _userId = agentContext.ParentId;
        _rootPath = options.Value.RootPath;
        _user = userStorage.GetOrCreateUser(_userId).GetAwaiter().GetResult();
        
    }

    /// <inheritdoc />
    public string UserId => _userId;

    /// <inheritdoc />
    public string AgentRoot => _rootPath;

    /// <inheritdoc />
    public string UserRoot
    {
        get
        {
            var path = Path.Combine(_rootPath, "users", _user.SelfHostUserId);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <inheritdoc />
    public string WorkingDirectory
    {
        get
        {
            var path = Path.Combine(UserRoot, "workspace");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>
    /// 确保所有必要目录存在
    /// </summary>
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(WorkingDirectory);
    }
}
