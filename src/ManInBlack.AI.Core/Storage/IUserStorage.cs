namespace ManInBlack.AI.Core.Storage;

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

    /// <summary>
    /// 为用户创建新的会话 ID
    /// </summary>
    /// <param name="userId"></param>
    /// <returns>新会话 ID</returns>
    Task<string> CreateNewSessionIdAsync(string userId);
}
