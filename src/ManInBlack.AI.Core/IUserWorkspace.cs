using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Core;

/// <summary>
/// 用户工作空间接口，提供用户级数据的存储和访问能力
/// </summary>
public interface IUserWorkspace
{
    /// <summary>
    /// 用户标识
    /// </summary>
    string UserId { get; }

    string AgentRoot { get; }

    string UserRoot { get; }

    /// <summary>
    /// 工作目录路径，命令行工具在此目录下执行
    /// </summary>
    string WorkingDirectory { get; }

    /// <summary>
    /// 初始化工作空间并加载历史消息
    /// </summary>
    /// <returns>历史聊天消息列表</returns>
    List<ChatMessage> Initialize();

    /// <summary>
    /// 追加一条历史聊天消息
    /// </summary>
    void AppendHistoryChatMessage(ChatMessage message);

    /// <summary>
    /// 创建新的会话，后续读写将切换到新会话
    /// </summary>
    void NewSession();
}