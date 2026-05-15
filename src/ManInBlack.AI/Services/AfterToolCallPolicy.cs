using ManInBlack.AI.Abstraction.Storage;

namespace ManInBlack.AI.Services;

/// <summary>
/// 默认检查点策略：每轮工具调用后和 session 结束时都保存
/// </summary>
public class AfterToolCallPolicy : ICheckpointPolicy
{
    public bool ShouldSave(string phase) => phase is "AfterToolCall" or "SessionEnd";
}
