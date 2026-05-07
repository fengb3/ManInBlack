using System.Text;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Agent;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Agent;

/// <summary>
/// Agent 工厂，封装 agent 创建的 5 步拼装：作用域、模型、管道、上下文、工具
/// </summary>
public class AgentFactory(
    IServiceProvider rootServiceProvider,
    IAgentRegistry agentRegistry,
    IHttpClientFactory httpClientFactory,
    ModelChoice defaultModelChoice) : IAgentFactory
{
    /// <inheritdoc />
    public Task<AgentResult> RunAsync(string agentName, string input, AgentContext parentContext, CancellationToken ct)
    {
        var definition = agentRegistry.Get(agentName)
            ?? throw new InvalidOperationException($"未找到名为 '{agentName}' 的 Agent 定义");
        return RunAsync(definition, input, parentContext, ct);
    }

    /// <inheritdoc />
    public async Task<AgentResult> RunAsync(AgentDefinition definition, string input, AgentContext parentContext, CancellationToken ct)
    {
        try
        {
            // 1. 创建子作用域
            using var scope = rootServiceProvider.CreateScope();
            var sp = scope.ServiceProvider;

            // 2. 解析模型并创建 ChatClient
            var modelChoice = definition.Model != null
                ? ToModelChoice(definition.Model)
                : defaultModelChoice;
            var chatClient = ChatClientProviderExtensions.CreateChatClient(httpClientFactory, modelChoice);

            // 3. 构建管道
            var pipelineBuilder = new AgentPipelineBuilder();
            pipelineBuilder = definition.PipelineName switch
            {
                "Default" => pipelineBuilder.UseDefault(),
                "Coder" => pipelineBuilder.UseCoder(),
                "Shell" => pipelineBuilder.UseShell(),
                "Analyst" => pipelineBuilder.UseAnalyst(),
                _ => pipelineBuilder.UseSimple(),
            };
            var pipeline = pipelineBuilder.Build(sp, chatClient);

            // 4. 获取子上下文
            var childContext = sp.GetRequiredService<AgentContext>();

            // 5. 配置子上下文
            childContext.AgentId = Guid.NewGuid().ToString();
            childContext.ParentId = parentContext.AgentId;
            childContext.ParentType = "Agent";
            childContext.SessionId = parentContext.SessionId;
            childContext.SystemPrompt = definition.Instructions;
            childContext.UserInput = input;
            childContext.CancellationToken = ct;

            // 6. 初始化选项
            childContext.Options = new ChatOptions();

            // 7. 运行管道并收集输出
            var outputBuilder = new StringBuilder();
            await foreach (var update in pipeline(childContext).WithCancellation(ct))
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent text)
                        outputBuilder.Append(text.Text);
                }
            }

            // 8. 返回成功结果
            return AgentResult.Ok(outputBuilder.ToString(), childContext.AccumulatedUsage);
        }
        catch (Exception ex)
        {
            return AgentResult.Fail(ex);
        }
    }

    /// <summary>
    /// 将 AgentModelOptions 转换为 ModelChoice
    /// </summary>
    private static ModelChoice ToModelChoice(AgentModelOptions options)
    {
        ModelProvider provider = options.ProviderName switch
        {
            "OpenAI" => new OpenAIProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.openai.com" : options.BaseUrl },
            "Anthropic" => new AnthropicProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.anthropic.com" : options.BaseUrl },
            "Gemini" => new GeminiProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://generativelanguage.googleapis.com" : options.BaseUrl },
            "DeepSeek" => new DeepSeekProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.deepseek.com" : options.BaseUrl },
            "Kimi-cn" => new KimiCNProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.moonshot.cn" : options.BaseUrl },
            "Kimi-ai" => new KimiAIProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.moonshot.ai" : options.BaseUrl },
            "Qwen" => new QwenProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://dashscope.aliyuncs.com/compatible-mode" : options.BaseUrl },
            "Zhipu" => new ZhipuProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://open.bigmodel.cn/api/paas/v4" : options.BaseUrl },
            "ZhipuCodingPlan" => new ZhipuCodingPlanProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://open.bigmodel.cn/api/coding/paas/v4" : options.BaseUrl },
            "Yi" => new YiProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.lingyiwanwu.com" : options.BaseUrl },
            "Baichuan" => new BaichuanProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.baichuan-ai.com" : options.BaseUrl },
            "StepFun" => new StepFunProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.stepfun.com" : options.BaseUrl },
            "Spark" => new SparkProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://spark-api-open.xf-yun.com" : options.BaseUrl },
            "Doubao" => new DoubaoProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://ark.cn-beijing.volces.com/api" : options.BaseUrl },
            "MiniMax" => new MiniMaxProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.minimax.chat" : options.BaseUrl },
            // 未识别的提供商，回退到 OpenAI 兼容模式
            _ => new OpenAIProvider { ApiKey = options.ApiKey, BaseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "https://api.openai.com" : options.BaseUrl }
        };
        return new ModelChoice { Provider = provider, ModelId = options.ModelId };
    }

}
