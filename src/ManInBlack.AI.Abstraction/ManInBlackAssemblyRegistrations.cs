using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Abstraction;

/// <summary>
/// 跨程序集服务注册表。源生成器在每个含 [AiTool]/[SlashCommand]/[ServiceRegister] 的程序集中
/// 生成模块初始化器（ModuleInitializer），程序集被加载时自动把本程序集的注册委托登记到此表；
/// <c>AddManInBlack()</c> 调用 <see cref="ApplyTo"/> 统一应用全部委托，
/// 实现"标记即注册、自动组合跨程序集的工具/命令/服务"。
/// </summary>
public static class ManInBlackAssemblyRegistrations
{
    private static readonly object Gate = new();
    private static readonly List<Action<IServiceCollection>> Registrations = [];

    /// <summary>
    /// 登记一个注册委托。由源生成器生成的模块初始化器调用，业务代码无需调用。
    /// </summary>
    public static void Register(Action<IServiceCollection> registration)
    {
        lock (Gate)
            Registrations.Add(registration);
    }

    /// <summary>
    /// 按登记顺序应用当前已加载程序集登记的全部注册委托。
    /// </summary>
    public static void ApplyTo(IServiceCollection services)
    {
        Action<IServiceCollection>[] snapshot;
        lock (Gate)
            snapshot = [.. Registrations];

        foreach (var registration in snapshot)
            registration(services);
    }
}
