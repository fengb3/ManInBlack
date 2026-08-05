namespace ManInBlack.AI.Abstraction.Storage;

public interface IUserStorage
{
    /// <summary>
    /// 获取或创建用户
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    Task<UserEntry> GetOrCreateUser(string userId);

    /// <summary>
    /// 保存用户信息
    /// </summary>
    /// <param name="userEntry"></param>
    /// <returns></returns>
    Task SaveUserAsync(UserEntry userEntry);

    /// <summary>为用户创建新会话并写入 Sessions 表，返回 SessionId。</summary>
    Task<string> CreateNewSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive);

    /// <summary>返回指定来源的最新会话 Id（按 LastAt 倒序），无则 null。</summary>
    Task<string?> GetLatestSessionIdAsync(string userId, SessionSource source = SessionSource.Interactive);
}
