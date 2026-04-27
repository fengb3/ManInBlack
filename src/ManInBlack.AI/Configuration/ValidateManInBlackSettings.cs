using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

public class ValidateManInBlackSettings : IValidateOptions<ManInBlackSettings>
{
    public ValidateOptionsResult Validate(string? name, ManInBlackSettings options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return ValidateOptionsResult.Fail("settings.json 缺少 ApiKey 配置");

        return ValidateOptionsResult.Success;
    }
}
