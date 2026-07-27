namespace ManInBlack.AI.Abstraction.Attributes;

/// <summary>标记一个方法为斜杠命令,仅供源生成器识别。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SlashCommandAttribute(string name, string description) : Attribute
{
    /// <summary>命令名(不含前导 /)。</summary>
    public string Name { get; } = name;

    /// <summary>一句话描述,用于 /help。</summary>
    public string Description { get; } = description;

    /// <summary>别名(同样不含 /)。</summary>
    public string[] Aliases { get; set; } = [];
}
