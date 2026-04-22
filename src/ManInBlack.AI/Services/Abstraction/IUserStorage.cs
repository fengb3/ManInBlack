namespace ManInBlack.AI.Services.Abstraction;

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
    /// 用户工作空间
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    string GetUserWorkingDir(string userId);
}