using ManInBlack.AI.Abstraction.Attributes;

namespace ManInBlack.AI.Tests.Integration;

/// <summary>
/// 集成测试用的 [AiTool] 工具。类必须 partial，供源生成器生成 handler；
/// 非静态工具类需通过 [ServiceRegister] 注册到 DI，供生成的 handler 解析实例。
/// </summary>
[ServiceRegister.Scoped]
public partial class CalculatorTool
{
    /// <summary>把两个整数相加</summary>
    /// <param name="a">第一个整数</param>
    /// <param name="b">第二个整数</param>
    /// <returns>两数之和</returns>
    [AiTool]
    public int Add(int a, int b) => a + b;
}
