using ManInBlack.AI.Abstraction.Tools;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

public class ToolArgumentJsonOptionsTests
{
    [Fact]
    public void Default_大小写不敏感()
    {
        Assert.True(ToolArgumentJsonOptions.Default.PropertyNameCaseInsensitive);
    }
}
