using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

public class ValidateManInBlackSettings : IValidateOptions<ManInBlackSettings>
{
    private static readonly HashSet<string> ValidSchemas = ["OpenAI", "Anthropic", "Gemini"];

    public ValidateOptionsResult Validate(string? name, ManInBlackSettings options)
    {
        if (options.Providers.Count == 0)
            return ValidateOptionsResult.Fail("settings.json 缺少 Providers 配置，至少需要一个 provider");

        if (!options.ModelChoices.ContainsKey("default"))
            return ValidateOptionsResult.Fail("settings.json 的 ModelChoices 必须包含一个 \"default\" 条目");

        foreach (var (providerName, provider) in options.Providers)
        {
            if (!ValidSchemas.Contains(provider.Schema))
                return ValidateOptionsResult.Fail($"Provider \"{providerName}\" 的 Schema \"{provider.Schema}\" 不合法，只允许：OpenAI、Anthropic、Gemini");

            if (string.IsNullOrWhiteSpace(provider.ApiKey))
                return ValidateOptionsResult.Fail($"Provider \"{providerName}\" 缺少 ApiKey");
        }

        foreach (var (choiceName, choice) in options.ModelChoices)
        {
            if (!options.Providers.ContainsKey(choice.ProviderName))
                return ValidateOptionsResult.Fail($"ModelChoice \"{choiceName}\" 引用的 Provider \"{choice.ProviderName}\" 不存在");

            if (string.IsNullOrWhiteSpace(choice.ModelId))
                return ValidateOptionsResult.Fail($"ModelChoice \"{choiceName}\" 缺少 ModelId");
        }

        // 校验 Agents 配置
        foreach (var (agentName, agent) in options.Agents)
        {
            if (string.IsNullOrWhiteSpace(agent.PipelineName))
                return ValidateOptionsResult.Fail($"Agent \"{agentName}\" 的 PipelineName 不能为空");

            if (agent.SubAgents.Contains(agentName))
                return ValidateOptionsResult.Fail($"Agent \"{agentName}\" 不能将自己列为子 Agent");

            // TODO: 检测间接循环引用（如 A→B→A），当前仅捕获直接自引用

            foreach (var subAgentName in agent.SubAgents)
            {
                if (!options.Agents.ContainsKey(subAgentName))
                    return ValidateOptionsResult.Fail($"Agent \"{agentName}\" 的 SubAgents 引用了不存在的 Agent \"{subAgentName}\"");
            }

            if (!string.IsNullOrEmpty(agent.ModelChoiceName)
                && !options.ModelChoices.ContainsKey(agent.ModelChoiceName))
                return ValidateOptionsResult.Fail($"Agent \"{agentName}\" 的 ModelChoiceName \"{agent.ModelChoiceName}\" 在 ModelChoices 中不存在");
        }

        return ValidateOptionsResult.Success;
    }
}
