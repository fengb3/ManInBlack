using System.Numerics;
using ManInBlack.AI.Tools;

namespace Playground;

public class SimpleMathTools
{
    /// <summary>
    /// Add 2 number
    /// </summary>
    /// <param name="a">lhs number</param>
    /// <param name="b">rhs number</param>
    /// <returns>the sum of 2 number</returns>
    [Tool]
    public int Add(int a, int b)
    {
        return a + b;
    }
    
    /// <summary>
    /// Add 2 number
    /// </summary>
    /// <param name="a">lhs number</param>
    /// <param name="b">rhs number</param>
    /// <returns>a minus b</returns>
    [Tool]
    public int Sub(int a, int b)
    {
        return a - b;
    }
    
    [Tool]
    public static int Mul(int a, int b)
    {
        return a * b;
    }

    [Tool]
    public static int Div(int a, int b)
    {
        return a / b;
    }
}

public static class ComplexMathTools
{
    [Tool]
    public static int Log(Complex a, Complex b)
    {
        return (int)(Complex.Log(a) / Complex.Log(b)).Real;
    }

    [Tool]
    public static int Log10(Complex a, Complex b)
    {
        return (int)(Complex.Log10(a) / Complex.Log10(b)).Real;
    }


}

public class LoggingCallFilter : ToolCallFilter
{
    public override Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
    {
        Console.WriteLine("Before executing tool");
        var result = next(context);
        Console.WriteLine("After executing tool");
        return result;
    }
}