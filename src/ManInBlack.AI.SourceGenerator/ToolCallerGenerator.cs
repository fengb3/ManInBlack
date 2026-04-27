using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ManInBlack.AI.SourceGenerator;

[Generator]
public sealed class ToolCallerGenerator : IIncrementalGenerator
{
    //TODO : 用类型 type 对象获取全程而不是字符串对比，避免命名空间变更导致生成器失效
    private const string ToolAttributeFullName = "ManInBlack.AI.Abstraction.Attributes.AiToolAttribute";
    private const string HasFilterAttributePrefix = "ManInBlack.AI.Abstraction.Attributes.AiTool.HasFilterAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. 扫描所有有属性的 MethodDeclarationSyntax
        var toolMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetToolMethodModel(ctx))
            .Where(static m => m is not null)
            .Collect();

        // 2. 获取 RootNamespace
        var namespaceProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns) ? ns : "Generated");

        // 3. 合并并生成
        var combined = toolMethods.Combine(namespaceProvider);

        context.RegisterSourceOutput(combined, (spc, source) =>
        {
            var (methods, ns) = source;
            var methodList = methods.Where(m => m is not null).Select(m => m!).ToList();

            // 没有任何 [AiTool] 方法时跳过生成
            if (methodList.Count == 0)
                return;

            // 解析命名冲突：同名方法加类名前缀
            ResolveToolNames(methodList);

            var sourceText = ToolCallerEmitter.Emit(ns, methodList);
            spc.AddSource("ToolExecutor.g.cs", SourceText.From(sourceText, Encoding.UTF8));
        });
    }

    private static ToolMethodModel? GetToolMethodModel(GeneratorSyntaxContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;

        if (methodSymbol is null)
            return null;

        // 检查是否标记了 [Tool] 属性
        if (!methodSymbol.GetAttributes().Any(attr =>
                attr.AttributeClass is not null &&
                attr.AttributeClass.ToDisplayString() == ToolAttributeFullName))
            return null;

        // 跳过泛型方法
        if (methodSymbol.IsGenericMethod)
            return null;

        var containingType = methodSymbol.ContainingType;

        // 跳过开放泛型类型
        if (containingType.TypeParameters.Length > 0 &&
            containingType.TypeArguments.Length == 0)
            return null;

        var fullyQualifiedFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        var parameters = methodSymbol.Parameters.Select(p => new ToolParameterModel
        {
            Name = p.Name,
            Type = p.Type.ToDisplayString(fullyQualifiedFormat),
            FullTypeName = p.Type.ToDisplayString(fullyQualifiedFormat),
            IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated ||
                         p.Type.NullableAnnotation == NullableAnnotation.Annotated,
            IsValueType = p.Type.IsValueType,
            HasDefaultValue = p.HasExplicitDefaultValue,
            DefaultValueExpr = p.HasExplicitDefaultValue
                ? FormatDefaultValue(p.ExplicitDefaultValue, p.Type)
                : null
        }).ToList();

        // 检测 async 返回类型（Task<T>, ValueTask<T>, Task, ValueTask）
        var (isAsync, actualReturnType, returnsVoid) = UnwrapAsyncReturnType(methodSymbol.ReturnType, fullyQualifiedFormat);

        // 提取 [AiTool.HasFilter<T...>] 属性中的 filter 类型
        var filterTypes = new List<string>();
        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (attr.AttributeClass is not null &&
                attr.AttributeClass.ToDisplayString().StartsWith(HasFilterAttributePrefix))
            {
                foreach (var typeArg in attr.AttributeClass.TypeArguments)
                {
                    filterTypes.Add(typeArg.ToDisplayString(fullyQualifiedFormat));
                }
            }
        }

        return new ToolMethodModel
        {
            MethodName = methodSymbol.Name,
            ContainingTypeName = containingType.ToDisplayString(fullyQualifiedFormat),
            ContainingTypeShortName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            FullyQualifiedTypeName = containingType.ToDisplayString(fullyQualifiedFormat),
            IsStatic = methodSymbol.IsStatic,
            IsAsync = isAsync,
            ReturnsVoid = returnsVoid,
            ReturnType = actualReturnType,
            Parameters = parameters,
            FilterTypes = filterTypes
        };
    }

    private static string? FormatDefaultValue(object? value, ITypeSymbol type)
    {
        if (value is null) return "null";
        if (value is bool b) return b ? "true" : "false";
        if (value is string s) return $"\"{s}\"";
        if (value is char c) return $"'{c}'";
        if (value.GetType().IsEnum) return $"{type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{value}";
        return value.ToString();
    }

    /// <summary>
    /// 解析命名冲突：如果多个不同类中的方法同名，则加上类名前缀
    /// </summary>
    private static void ResolveToolNames(List<ToolMethodModel> methods)
    {
        // 按方法名分组
        var groups = methods.GroupBy(m => m.MethodName).ToList();

        foreach (var group in groups)
        {
            if (group.Count() > 1)
            {
                // 有冲突，使用 ClassName.MethodName
                foreach (var method in group)
                {
                    method.ToolName = $"{method.ContainingTypeShortName}.{method.MethodName}";
                }
            }
            else
            {
                // 无冲突，直接用方法名
                group.First().ToolName = group.Key;
            }
        }
    }

    private static (bool isAsync, string returnType, bool returnsVoid) UnwrapAsyncReturnType(
        ITypeSymbol returnType, SymbolDisplayFormat format)
    {
        if (returnType is not INamedTypeSymbol named)
            return (false, returnType.ToDisplayString(format), returnType.SpecialType == SpecialType.System_Void);

        if (!IsTaskType(named))
            return (false, returnType.ToDisplayString(format), returnType.SpecialType == SpecialType.System_Void);

        // Task<T> or ValueTask<T>
        if (named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var innerType = named.TypeArguments[0];
            return (true, innerType.ToDisplayString(format), false);
        }

        // Task or ValueTask (non-generic)
        return (true, "void", true);
    }

    private static bool IsTaskType(INamedTypeSymbol type)
    {
        var name = type.ConstructedFrom.Name;
        var ns = type.ConstructedFrom.ContainingNamespace?.ToDisplayString();
        return ns == "System.Threading.Tasks" && name is "Task" or "ValueTask";
    }
}
