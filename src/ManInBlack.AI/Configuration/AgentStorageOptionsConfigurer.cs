using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 从合并后的 ManInBlackSettings.Storage 映射到运行期 AgentStorageOptions。
/// </summary>
internal sealed class AgentStorageOptionsConfigurer(IOptions<ManInBlackSettings> settings)
    : IConfigureOptions<AgentStorageOptions>
{
    public void Configure(AgentStorageOptions options)
    {
        var storage = settings.Value.Storage;
        if (storage.RootPath is not null)
            options.RootPath = storage.RootPath;
        if (storage.Workspace is not null)
            options.Workspace = storage.Workspace;
    }
}
