using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Agent;

/// <summary>
/// 从 settings.json 的 Agents 配置节加载 Agent 定义并注册到 DI 容器。
/// 未知管道名称会回退到 "Simple"，未知模型引用会使用默认模型（不抛异常）。
/// </summary>
public static class AgentConfigurationLoader
{
    /// <summary>
    /// 从 <see cref="ManInBlackSettings.Agents"/> 读取所有 Agent 配置，
    /// 构建 <see cref="AgentDefinition"/> 并以 Singleton 注册到 <paramref name="services"/>。
    /// </summary>
    public static void LoadFromConfiguration(IServiceCollection services, ManInBlackSettings settings)
    {
        if (settings.Agents == null) return;

        foreach (var (name, cfg) in settings.Agents)
        {
            var builder = new AgentBuilder(name)
                .WithDescription(cfg.Description)
                .WithInstructions(cfg.Instructions);

            // 映射管道名称
            if (!string.IsNullOrEmpty(cfg.Pipeline))
            {
                builder.WithPipeline(cfg.Pipeline);
            }

            // 映射模型引用：从 Models 字典查找对应的连接参数
            if (cfg.Model is not null
                && settings.Models is not null
                && settings.Models.TryGetValue(cfg.Model, out var mc))
            {
                builder.WithModel(new AgentModelOptions
                {
                    ProviderName = mc.Provider,
                    ApiKey = mc.ApiKey,
                    BaseUrl = mc.BaseUrl ?? string.Empty,
                    ModelId = mc.ModelId,
                });
            }

            services.AddSingleton(builder.Build());
        }
    }
}
