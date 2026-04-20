using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ManInBlack.AI.SourceGenerator;

/// <summary>
/// 为每个包含 [AiTool] 方法的 partial 类自动生成一个 Middleware，
/// 该 Middleware 将工具声明注入到 AgentContext.Options.Tools 中
/// </summary>
[Generator]
public sealed class ToolMiddlewareGenerator : IIncrementalGenerator
{
    private const string ToolAttributeFullName = "ManInBlack.AI.Core.Attributes.AiToolAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. 扫描所有有 [AiTool] 属性的方法
        var toolClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetToolMiddlewareModel(ctx))
            .Where(static m => m is not null)
            .Collect()
            .Select(static (models, _) =>
            {
                // 去重：同一类只需生成一个 middleware
                return models
                    .Where(m => m is not null)
                    .Select(m => m!)
                    .GroupBy(m => m.FullyQualifiedTypeName)
                    .Select(g => g.First())
                    .Where(m => m.IsPartialClass) // 只为 partial 类生成
                    .ToList();
            });

        // 2. 获取 RootNamespace
        var namespaceProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns) ? ns : "Generated");

        // 3. 合并并生成
        var combined = toolClasses.Combine(namespaceProvider);

        context.RegisterSourceOutput(combined, (spc, source) =>
        {
            var (models, ns) = source;
            if (models.Count == 0)
                return;

            var sourceText = ToolMiddlewareEmitter.Emit(ns, models);
            spc.AddSource("ToolMiddlewares.g.cs", SourceText.From(sourceText, Encoding.UTF8));
        });
    }

    private static ToolMiddlewareModel? GetToolMiddlewareModel(GeneratorSyntaxContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;

        if (methodSymbol is null)
            return null;

        // 检查是否标记了 [AiTool] 属性
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

        // 检查所属类是否为 partial
        var classDecl = methodDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        var isPartialClass = classDecl is not null &&
                             classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

        return new ToolMiddlewareModel
        {
            ContainingTypeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            FullyQualifiedTypeName = containingType.ToDisplayString(fullyQualifiedFormat),
            ContainingNamespace = containingType.ContainingNamespace.ToDisplayString(
                new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)),
            IsPartialClass = isPartialClass
        };
    }
}
