namespace ManInBlack.AI.Configuration;

/// <summary>
/// 一条对 ManInBlackSettings 的配置贡献。在 IOptions 首次 resolve 时按注册顺序应用。
/// </summary>
internal interface IManInBlackContribution
{
    void Apply(ManInBlackSettings settings);
}
