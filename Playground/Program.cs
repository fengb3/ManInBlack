// See https://aka.ms/new-console-template for more information

// 在任意 .NET interactive 或测试中

using System;
using System.Linq;
using Microsoft.Extensions.AI;

// Print properties of AITool
Console.WriteLine("--- AITool properties ---");
foreach (var line in typeof(AITool).GetProperties().Select(p => $"{p.PropertyType.Name} {p.Name}"))
{
	Console.WriteLine(line);
}

Console.WriteLine("--- AIFunction properties ---");
foreach (var line in typeof(AIFunction).GetProperties().Select(p => $"{p.PropertyType.Name} {p.Name}"))
{
	Console.WriteLine(line);
}

Console.WriteLine("--- FunctionCallContent constructors ---");
foreach (var line in typeof(FunctionCallContent).GetConstructors().Select(c => c.ToString()))
{
	Console.WriteLine(line);
}

Console.WriteLine("--- FunctionResultContent constructors ---");
foreach (var line in typeof(FunctionResultContent).GetConstructors().Select(c => c.ToString()))
{
	Console.WriteLine(line);
}
