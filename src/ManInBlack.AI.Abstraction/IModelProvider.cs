namespace ManInBlack.AI.Abstraction;

/// <summary>
/// 聊天客户端提供商接口
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// 提供商名称
    /// </summary>
    string ProviderName { get; }

    string ApiKey { get; set; }

    string BaseUrl { get; set; }

    string CompatibleWith { get; }
}

public abstract class ModelProvider : IModelProvider
{
    public abstract string ProviderName { get; }
    public abstract string BaseUrl { get; set; }
    public abstract string CompatibleWith { get; }

    public string ApiKey { get; set; } = null!;
}
