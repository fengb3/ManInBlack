using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ManInBlack.AI.Abstraction.Attributes; // 触发加载 Abstraction 程序集，供引用解析

namespace ManInBlack.AI.Tests.Tools;

/// <summary>
/// 用 Roslyn CSharpGeneratorDriver 直接跑源生成器，用于诊断/生成源测试。
/// 说明：测试项目同时引用 SG（Analyzer）与 NuGet Roslyn 4.11.0，而 .NET SDK 10
/// 自带更新版本的编译器 Roslyn，编译期 CSharpGeneratorDriver.Create 的
/// IIncrementalGenerator 重载与 SG 类型标识会与 NuGet 副本不一致（CS1503）。
/// 因此生成器实例与驱动构造全部走反射，彻底规避编译期类型标识冲突。
/// </summary>
public static class GeneratorDriverHelper
{
    private static readonly string GeneratorAssemblyPath = LocateGeneratorAssembly();

    public static GeneratorDriver Run(string source)
    {
        _ = typeof(AiToolAttribute).Assembly; // 确保 Abstraction 程序集已加载

        var parseOptions = CSharpParseOptions.Default;
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDriverTest",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: GetReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = CreateGenerator();
        // 反射构造 CSharpGeneratorDriver 并 RunGenerators，避免编译期对
        // IIncrementalGenerator[] 重载的绑定（SDK-Roslyn 与 NuGet-Roslyn 类型标识冲突）。
        var driver = CreateDriver(generator);
        // RunGenerators(Compilation, CancellationToken) 声明在基类 GeneratorDriver 上。
        var runGenerators = driver.GetType().GetMethod(
            "RunGenerators", new[] { typeof(Compilation), typeof(System.Threading.CancellationToken) })!;
        return (GeneratorDriver)runGenerators.Invoke(driver, new object?[] { compilation, default(System.Threading.CancellationToken) })!;
    }

    /// <summary>反射加载 SG 程序集中的 ToolCallerGenerator 并构造实例。</summary>
    private static object CreateGenerator()
    {
        var asm = Assembly.LoadFrom(GeneratorAssemblyPath);
        var type = asm.GetType("ManInBlack.AI.SourceGenerator.ToolCallerGenerator")!;
        return Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// 反射调用 CSharpGeneratorDriver.Create(params IIncrementalGenerator[])。
    /// 走反射以规避编译期 SDK-Roslyn 与 NuGet-Roslyn 的 IIncrementalGenerator 类型标识冲突。
    /// </summary>
    private static object CreateDriver(object generator)
    {
        var driverType = typeof(CSharpGeneratorDriver);

        // 形参仅一个、元素为 IIncrementalGenerator[] 的 Create 重载。
        var create = driverType.GetMethods()
            .First(m =>
            {
                if (m.Name != "Create") return false;
                var ps = m.GetParameters();
                if (ps.Length != 1) return false;
                var p0 = ps[0].ParameterType;
                return p0.IsArray && p0.GetElementType()!.Name == "IIncrementalGenerator";
            });

        var elementType = create.GetParameters()[0].ParameterType.GetElementType()!;
        var array = Array.CreateInstance(elementType, 1);
        array.SetValue(generator, 0); // 运行期两份 IIncrementalGenerator 为同一副本，可直接赋值
        return create.Invoke(null, new object[] { array })!;
    }

    /// <summary>定位 SG 生成的程序集输出路径（与测试在同一解决方案，相对输出目录回溯）。</summary>
    private static string LocateGeneratorAssembly()
    {
        // 测试运行目录通常为 .../test/ManInBlack.AI.Tests/bin/Debug/net10.0
        var dir = AppContext.BaseDirectory;
        var fileName = "ManInBlack.AI.SourceGenerator.dll";
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate)) return candidate;
            // 也尝试 SG 的 netstandard2.0 输出
            var probe = Path.Combine(dir, "..", "..", "..", "..", "..",
                "src", "ManInBlack.AI.SourceGenerator", "bin", "Debug", "netstandard2.0", fileName);
            if (File.Exists(probe)) return Path.GetFullPath(probe);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("未找到 ManInBlack.AI.SourceGenerator.dll", fileName);
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                yield return MetadataReference.CreateFromFile(asm.Location);
        }
    }
}
