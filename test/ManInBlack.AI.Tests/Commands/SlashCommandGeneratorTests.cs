using ManInBlack.AI;            // AddSlashCommands() extension (generated, internal)
using ManInBlack.AI.Commands;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ManInBlack.AI.Tests.Commands;

public class SlashCommandGeneratorTests
{
    [Fact]
    public void AddSlashCommands_RegistersNewAndHelp_WithAliases()
    {
        var services = new ServiceCollection();
        services.AddSlashCommands();
        var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<SlashCommandRegistry>();

        Assert.True(registry.TryGet("new", out _));
        Assert.True(registry.TryGet("clear", out _));
        Assert.True(registry.TryGet("reset", out _));
        Assert.True(registry.TryGet("help", out _));

        var names = registry.Commands.Select(c => c.Name).ToList();
        Assert.Contains("new", names);
        Assert.Contains("help", names);
    }
}
