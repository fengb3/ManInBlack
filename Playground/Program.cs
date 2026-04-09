using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Playground;

// 构建 DI 容器
var services = new ServiceCollection();
services.AddTransient<SimpleMathTools>();
var sp = services.BuildServiceProvider();

var caller = new ToolCaller(sp);

Console.WriteLine("--- ToolCaller test ---");

var addResult = caller.CallTool("Add", new Dictionary<string, object?> { {"a", 3}, {"b", 5} });
Console.WriteLine($"Add(3, 5) = {addResult}");

var subResult = caller.CallTool("Sub", new Dictionary<string, object?> { {"a", 10}, {"b", 4} });
Console.WriteLine($"Sub(10, 4) = {subResult}");

// 静态方法不需要 DI 解析
var mulResult = caller.CallTool("Mul", new Dictionary<string, object?> { {"a", 3}, {"b", 7} });
Console.WriteLine($"Mul(3, 7) = {mulResult}");

// 未知 tool
try
{
    caller.CallTool("Unknown", new Dictionary<string, object?>());
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Unknown tool error: {ex.Message}");
}

// 缺少参数
try
{
    caller.CallTool("Add", new Dictionary<string, object?> { {"a", 3} });
}
catch (ArgumentNullException ex)
{
    Console.WriteLine($"Missing param error: {ex.ParamName}");
}

// DI 未注册类型
var callerNoService = new ToolCaller(new ServiceCollection().BuildServiceProvider());
try
{
    callerNoService.CallTool("Add", new Dictionary<string, object?> { {"a", 1}, {"b", 2} });
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"DI resolve error: {ex.Message}");
}
