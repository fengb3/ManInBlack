namespace ManInBlack.AI.Configuration;

/// <summary>
/// 把一个委托包装成贡献。供流式 AddXxx 方法使用。
/// </summary>
internal sealed class ActionContribution(Action<ManInBlackSettings> action) : IManInBlackContribution
{
    public void Apply(ManInBlackSettings settings) => action(settings);
}
