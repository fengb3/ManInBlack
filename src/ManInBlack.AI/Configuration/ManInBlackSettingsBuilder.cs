using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 收集全部 IManInBlackContribution，在 IOptions&lt;ManInBlackSettings&gt; 首次 resolve 时按注册顺序合并。
/// </summary>
internal sealed class ManInBlackSettingsBuilder(IEnumerable<IManInBlackContribution> contributions)
    : IConfigureOptions<ManInBlackSettings>
{
    public void Configure(ManInBlackSettings settings)
    {
        foreach (var contribution in contributions)
            contribution.Apply(settings);
    }
}
