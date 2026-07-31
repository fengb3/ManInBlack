namespace FeishuAdaptor.Tools;

/// <summary>
/// AskUser 工具的一个可选项。源生成器按公共可读属性生成 schema：
/// <see cref="Label"/> 非可空 → schema required；<see cref="Description"/>/<see cref="Value"/> 可空 → 可选。
/// </summary>
public record AskUserOption
{
    /// <summary>选项展示文案（必填，显示在按钮/选项上）。</summary>
    public string Label { get; set; } = "";

    /// <summary>辅助说明（可选）。</summary>
    public string? Description { get; set; }

    /// <summary>回传值（可选）；为空时回退为 <see cref="Label"/>。</summary>
    public string? Value { get; set; }

    public AskUserOption() { }

    public AskUserOption(string label)
    {
        Label = label;
        Value = label;
    }
}
