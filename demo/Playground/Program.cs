using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;
using Playground;
// using Playground.Tools;
// print every special folder
foreach (Environment.SpecialFolder folder in Enum.GetValues<Environment.SpecialFolder>())
{
    var path = Environment.GetFolderPath(folder);
    Console.WriteLine($"{folder,-30} {path}");
}
// // 构建 DI 容器
// var services = new ServiceCollection();
// services.AddTransient<SimpleMathTools>();
// var sp = services.BuildServiceProvider();
//
// var caller = new ToolExecutor(sp);
//
// Console.WriteLine("--- ToolExecutor test ---");
//
// // Add: 无 filter
// var addCtx = new ToolExecuteContext(sp) { ToolName = "Add", Arguments = new Dictionary<string, object?> { {"a", 3}, {"b", 5} } };
// await caller.CallTool(addCtx);
// Console.WriteLine($"Add(3, 5) = {addCtx.Result}");
//
// // Sub: 有 LoggingToolCallFilter + RetryCallFilter
// var subCtx = new ToolExecuteContext(sp) { ToolName = "Sub", Arguments = new Dictionary<string, object?> { {"a", 10}, {"b", 4} } };
// await caller.CallTool(subCtx);
// Console.WriteLine($"Sub(10, 4) = {subCtx.Result}");
//
// // 静态方法 Mul: RateLimitCallFilter + LoggingToolCallFilter
// var mulCtx = new ToolExecuteContext(sp) { ToolName = "Mul", Arguments = new Dictionary<string, object?> { {"a", 3}, {"b", 7} } };
// await caller.CallTool(mulCtx);
// Console.WriteLine($"Mul(3, 7) = {mulCtx.Result}");
//
// // Div: CacheCallFilter
// var divCtx = new ToolExecuteContext(sp) { ToolName = "Div", Arguments = new Dictionary<string, object?> { {"a", 20}, {"b", 4} } };
// await caller.CallTool(divCtx);
// Console.WriteLine($"Div(20, 4) = {divCtx.Result}");
//
// // 未知 tool
// try
// {
//     var unknownCtx = new ToolExecuteContext(sp) { ToolName = "Unknown", Arguments = new Dictionary<string, object?>() };
//     await caller.CallTool(unknownCtx);
// }
// catch (ArgumentException ex)
// {
//     Console.WriteLine($"Unknown tool error: {ex.Message}");
// }
//
// // 缺少参数
// try
// {
//     var missingCtx = new ToolExecuteContext(sp) { ToolName = "Add", Arguments = new Dictionary<string, object?> { {"a", 3} } };
//     await caller.CallTool(missingCtx);
// }
// catch (ArgumentNullException ex)
// {
//     Console.WriteLine($"Missing param error: {ex.ParamName}");
// }
//
// // DI 未注册类型
// var callerNoService = new ToolExecutor(new ServiceCollection().BuildServiceProvider());
// try
// {
//     var noServiceCtx = new ToolExecuteContext(new ServiceCollection().BuildServiceProvider()) { ToolName = "Add", Arguments = new Dictionary<string, object?> { {"a", 1}, {"b", 2} } };
//     await callerNoService.CallTool(noServiceCtx);
// }
// catch (InvalidOperationException ex)
// {
//     Console.WriteLine($"DI resolve error: {ex.Message}");
// }