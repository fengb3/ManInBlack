using ManInBlack.AI.Abstraction.Commands;

namespace ManInBlack.AI.Commands;

/// <summary>
/// 命令注册表:按命令名/别名(大小写不敏感)查找 <see cref="ICommandHandler"/>,
/// 并提供去重后的 <see cref="CommandInfo"/> 清单供 /help 展示。
/// </summary>
public sealed class SlashCommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _byKey;

    public SlashCommandRegistry(IEnumerable<ICommandHandler> handlers)
    {
        _byKey = new Dictionary<string, ICommandHandler>(StringComparer.OrdinalIgnoreCase);

        // 去重:同一 handler 实例只产出一条 CommandInfo
        var ordered = handlers.ToList();
        Commands = ordered
            .Select(h => new CommandInfo(h.CommandName, (IReadOnlyList<string>)h.Aliases, h.Description))
            .ToList();

        foreach (var h in ordered)
        {
            _byKey[h.CommandName] = h;
            foreach (var alias in h.Aliases)
                _byKey[alias] = h;
        }
    }

    public bool TryGet(string key, out ICommandHandler? handler)
        => _byKey.TryGetValue(key, out handler);

    /// <summary>去重后的命令清单(不含别名条目),供 /help。</summary>
    public IReadOnlyList<CommandInfo> Commands { get; }
}
