using ManInBlack.AI.Core.Tools;

namespace ManInBlack.AI.Core.Attributes;

/// <summary>
/// marks a method is an AI Function, this attribute is only a mark for source generator
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class AiToolAttribute : Attribute
{
}

public class AiTool
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HasFilterAttribute<T> : Attribute where T : ToolCallFilter;

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HasFilterAttribute<T1, T2> : Attribute where T1 : ToolCallFilter where T2 : ToolCallFilter;


    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HasFilterAttribute<T1, T2, T3> : Attribute where T1 : ToolCallFilter where T2 : ToolCallFilter where T3 : ToolCallFilter;


    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HasFilterAttribute<T1, T2, T3, T4> : Attribute where T1 : ToolCallFilter where T2 : ToolCallFilter where T3 : ToolCallFilter where T4 : ToolCallFilter;
}