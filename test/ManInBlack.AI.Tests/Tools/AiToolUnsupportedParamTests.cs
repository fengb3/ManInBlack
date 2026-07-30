using Microsoft.CodeAnalysis;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

public class AiToolUnsupportedParamTests
{
    private const string Source = """
using System.Collections.Generic;
using ManInBlack.AI.Abstraction.Attributes;
namespace TestNs;
public partial class BadTools
{
    /// <summary>bad</summary>
    /// <param name="map">dict</param>
    /// <returns>x</returns>
    [AiTool]
    public string DoStuff(Dictionary<string, string> map) => "x";
}
""";

    [Fact]
    public void Dictionary参数_报MIB014错误()
    {
        var result = GeneratorDriverHelper.Run(Source).GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Id == "MIB014" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void 对象参数_不报MIB014()
    {
        var supported = """
using ManInBlack.AI.Abstraction.Attributes;
namespace TestNs;
public partial class Opt { public string Label { get; set; } = ""; }
public partial class GoodTools
{
    /// <summary>ok</summary>
    /// <param name="o">opt</param>
    /// <returns>x</returns>
    [AiTool]
    public string DoStuff(Opt o) => o.Label;
}
""";
        var result = GeneratorDriverHelper.Run(supported).GetRunResult();
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "MIB014");
    }
}
