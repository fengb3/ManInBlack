// using System.Numerics;
// using ManInBlack.AI.Core.Attributes;
// using ManInBlack.AI.Core.Tools;
// using Microsoft.Extensions.Logging;
//
// namespace Playground.Tools;
//
// [ServiceRegister.Scoped]
// public partial class SimpleMathTools
// {
//     /// <summary>
//     /// Add 2 number
//     /// </summary>
//     /// <param name="a">lhs number</param>
//     /// <param name="b">rhs number</param>
//     /// <returns>the sum of 2 number</returns>
//     [AiTool]
//     public int Add(int a, int b)
//     {
//         return a + b;
//     }
//     
//     /// <summary>
//     /// Add 2 number
//     /// </summary>
//     /// <param name="a">lhs number</param>
//     /// <param name="b">rhs number</param>
//     /// <returns>a minus b</returns>
//     [AiTool]
//     [AiTool.HasFilter<LoggingToolCallFilter, RetryCallFilter>]
//     public int Sub(int a, int b)
//     {
//         return a - b;
//     }
//     
//     [AiTool]
//     [AiTool.HasFilter<RateLimitCallFilter>]
//     [AiTool.HasFilter<LoggingToolCallFilter>]
//     public static int Mul(int a, int b)
//     {
//         return a * b;
//     }
//
//     [AiTool]
//     [AiTool.HasFilter<CacheCallFilter>]
//     public static int Div(int a, int b)
//     {
//         return a / b;
//     }
// }
//
// public static partial class ComplexMathTools
// {
//     [AiTool]
//     [AiTool.HasFilter<LoggingToolCallFilter, RetryCallFilter>]
//     public static int Log(Complex a, Complex b)
//     {
//         return (int)(Complex.Log(a) / Complex.Log(b)).Real;
//     }
//
//     [AiTool]
//     [AiTool.HasFilter<LoggingToolCallFilter>]
//     [AiTool.HasFilter<RetryCallFilter>]
//     public static int Log10(Complex a, Complex b)
//     {
//         return (int)(Complex.Log10(a) / Complex.Log10(b)).Real;
//     }
// }
//
// public class LoggingToolCallFilter(ILogger<LoggingToolCallFilter> logger) : ToolCallFilter
// {
//     public override Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
//     {
//         logger.LogInformation("Before executing tool: " + context.ToolName);
//         var result = next(context);
//         logger.LogInformation("After executing tool: " + context.ToolName + ", result: " + context.Result);
//         return result;
//     }
// }
//
// public class RetryCallFilter : ToolCallFilter
// {
//     public int MaxRetryTimes { get; set; } = 3;
//     
//     public override async Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
//     {
//         int retryTimes = 0;
//         while (true)
//         {
//             try
//             {
//                 await next(context);
//                 break;
//             }
//             catch (Exception ex)
//             {
//                 retryTimes++;
//                 Console.WriteLine($"Error executing tool: {ex.Message}. Retry times: {retryTimes}");
//                 if (retryTimes >= MaxRetryTimes)
//                 {
//                     Console.WriteLine("Max retry times reached. Throwing exception.");
//                     throw;
//                 }
//             }
//         }
//     }
// }
//
// public class CacheCallFilter : ToolCallFilter
// {
//     private readonly Dictionary<string, object?> _cache = new();
//
//     public override async Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
//     {
//         var cacheKey = GenerateCacheKey(context);
//         if (_cache.TryGetValue(cacheKey, out var cachedResult))
//         {
//             Console.WriteLine("Cache hit for tool: " + context.ToolName);
//             context.Result = cachedResult;
//             return;
//         }
//
//         await next(context);
//         _cache[cacheKey] = context.Result;
//     }
//
//     private string GenerateCacheKey(ToolExecuteContext context)
//     {
//         var argsKey = string.Join("_", context.Arguments.Select(kv => $"{kv.Key}:{kv.Value}"));
//         return $"{context.ToolName}_{argsKey}";
//     }
// }
//
// public class RateLimitCallFilter : ToolCallFilter
// {
//     private readonly Dictionary<string, DateTime> _lastCallTimes = new();
//     public TimeSpan Cooldown { get; set; } = TimeSpan.FromMilliseconds(200);
//
//     public override async Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
//     {
//         var toolName = context.ToolName;
//         if (_lastCallTimes.TryGetValue(toolName, out var lastCallTime))
//         {
//             var timeSinceLastCall = DateTime.UtcNow - lastCallTime;
//             if (timeSinceLastCall < Cooldown)
//             {
//                 var waitTime = Cooldown - timeSinceLastCall;
//                 Console.WriteLine($"Rate limit hit for tool: {toolName}. Waiting for {waitTime.TotalSeconds} seconds.");
//                 await Task.Delay(waitTime);
//             }
//         }
//
//         await next(context);
//         _lastCallTimes[toolName] = DateTime.UtcNow;
//     }
// }
//
//
// public class LongResultReferenceCallFilter : ToolCallFilter
// {
//     public override async Task ExecuteAsync(ToolExecuteContext context, Func<ToolExecuteContext, Task> next)
//     {
//         // 过长result 保存到一个文件
//         await next(context);
//         
//         if(context.Result?.ToString()?.Length > 1024)
//         {
//             var filePath = $"result_{Guid.NewGuid()}.txt";
//             await File.WriteAllTextAsync(filePath, context.Result.ToString() ?? string.Empty);
//             context.Result = $"Result is too long, saved to file: {filePath}";
//         }
//     }
// }