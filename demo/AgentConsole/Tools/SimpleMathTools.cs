using System.Numerics;
using ManInBlack.AI.Attributes;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.Logging;

namespace AgentConsole.Tools;

[ServiceRegister.Scoped]
public partial class SimpleMathTools
{
    /// <summary>
    /// Add 2 number
    /// </summary>
    /// <param name="a">lhs number</param>
    /// <param name="b">rhs number</param>
    /// <returns>the sum of 2 number</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter>]
    public float Add(float a, float b)
    {
        return a + b;
    }
    
    /// <summary>
    /// Add 2 number
    /// </summary>
    /// <param name="a">lhs number</param>
    /// <param name="b">rhs number</param>
    /// <returns>a minus b</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter>]
    public float Sub(float a, float b)
    {
        return a - b;
    }
    
    /// <summary>
    /// mul 2 number
    /// </summary>
    /// <param name="a">lhs number</param>
    /// <param name="b">rhs number</param>
    /// <returns> float result of a * b</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter>]
    public float Mul(float a, float b)
    {
        return a * b;
    }

    /// <summary>
    /// div 2 numbers
    /// </summary>
    /// <param name="a">lhs num</param>
    /// <param name="b">rhs num</param>
    /// <returns>float result of a / b</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter>]
    public float Div(float a, float b)
    {
        return a / b;
    }
}


