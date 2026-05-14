using ManInBlack.AI.Abstraction;

namespace ManInBlack.AI.Services;

/// <summary>
/// 工作空间指向进程当前工作目录
/// </summary>
public class CurrentDirectoryWorkspace : IUserWorkspace
{
    public string WorkingDirectory => Directory.GetCurrentDirectory();
}
