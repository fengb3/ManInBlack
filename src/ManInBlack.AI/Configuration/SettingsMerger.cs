namespace ManInBlack.AI.Configuration;

/// <summary>
/// 把一份完整的 source ManInBlackSettings 按 key 合并进 target，供 UseJson/UseConfiguration 使用。
/// 字典按 key 覆盖（保留 target 中 source 没有的 key）；Hooks 累加；标量后者覆盖；Feishu 仅在非空时覆盖。
/// </summary>
internal static class SettingsMerger
{
    public static void Merge(ManInBlackSettings target, ManInBlackSettings source)
    {
        foreach (var kv in source.Providers)
            target.Providers[kv.Key] = kv.Value;

        foreach (var kv in source.ModelChoices)
            target.ModelChoices[kv.Key] = kv.Value;

        foreach (var kv in source.Agents)
            target.Agents[kv.Key] = kv.Value;

        foreach (var kv in source.McpServers)
            target.McpServers[kv.Key] = kv.Value;

        target.Hooks.AddRange(source.Hooks);

        // Storage：仅当 source 有实质内容（非全默认）时覆盖
        if (source.Storage is { } storage
            && (storage.RootPath is not null
                || storage.Workspace is not null
                || (storage.FileIsolation?.ReadableRoots.Count > 0)))
            target.Storage = storage;

        target.UseSandbox = source.UseSandbox;

        if (source.Feishu is not null)
            target.Feishu = source.Feishu;
    }
}
