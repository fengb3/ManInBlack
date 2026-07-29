using ManInBlack.AI.Abstraction.Middleware;

namespace ManInBlack.AI.Commands;

/// <summary>
/// 命令子系统在 <see cref="AgentContext.Items"/> 里使用的键,以及读取命令参数的扩展。
/// </summary>
public static class SlashCommandItems
{
    /// <summary>CommandMiddleware 派发前把解析好的命令参数(string[])写入此键。</summary>
    public const string Args = "__slashCommand.args";
}

public static class SlashCommandContextExtensions
{
    /// <summary>读取 CommandMiddleware 注入的位置参数;未注入时返回空数组。</summary>
    public static string[] GetCommandArgs(this AgentContext context)
        => context.Items.TryGetValue(SlashCommandItems.Args, out var v) && v is string[] a ? a : [];
}
