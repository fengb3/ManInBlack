namespace ManInBlack.AI;

/// <summary>
/// 显式指定类型化管道在字符串注册表中的名称。未标注时依次回退到
/// <c>public static string Name { get; }</c> 静态属性、<c>Type.Name</c>。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineNameAttribute : Attribute
{
    public PipelineNameAttribute(string name) => Name = name;

    public string Name { get; }
}
