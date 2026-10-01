using System.Reflection;

namespace ManInBlack.AI;

/// <summary>
/// 将 <see cref="IAgentPipeline"/> 实现类型解析为字符串名称。
/// </summary>
internal static class PipelineNameResolver
{
    /// <summary>
    /// 解析类型化管道名称。优先级：
    /// 1. <see cref="PipelineNameAttribute"/>
    /// 2. <c>public static string Name { get; }</c> 属性
    /// 3. <see cref="MemberInfo.Name"/>
    /// </summary>
    public static string Resolve<T>() where T : IAgentPipeline => Resolve(typeof(T));

    public static string Resolve(Type type)
    {
        var attr = type.GetCustomAttribute<PipelineNameAttribute>();
        if (attr is not null)
            return attr.Name;

        var nameProperty = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Static);
        if (nameProperty is not null && nameProperty.PropertyType == typeof(string))
        {
            var value = nameProperty.GetValue(null);
            if (value is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }

        return type.Name;
    }
}
