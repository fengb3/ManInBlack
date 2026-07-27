using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ManInBlack.AI.SourceGenerator;

[Generator]
public sealed class SlashCommandGenerator : IIncrementalGenerator
{
    private const string CommandAttributeFullName = "ManInBlack.AI.Abstraction.Attributes.SlashCommandAttribute";

    private static readonly DiagnosticDescriptor ClassNotPartial = new(
        id: "MIB020",
        title: "包含 [SlashCommand] 方法的类必须声明为 partial",
        messageFormat: "类 '{0}' 包含 [SlashCommand] 方法,必须声明为 partial",
        category: "SlashCommandDeclaration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor EmptyDescription = new(
        id: "MIB021",
        title: "[SlashCommand] 缺少 description",
        messageFormat: "[SlashCommand] '{0}' 的 description 为空",
        category: "SlashCommandDeclaration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateCommand = new(
        id: "MIB022",
        title: "命令名/别名重复",
        messageFormat: "命令名/别名 '{0}' 在 assembly 内重复",
        category: "SlashCommandDeclaration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commandMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetCommandMethodModel(ctx))
            .Where(static m => m is not null)
            .Collect();

        var namespaceProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns) ? ns : "Generated");

        var combined = commandMethods.Combine(namespaceProvider);

        context.RegisterSourceOutput(combined, (spc, source) =>
        {
            var (methods, ns) = source;
            var methodList = methods.Where(m => m is not null).Select(m => m!).ToList();

            if (methodList.Count == 0)
                return;

            ReportDiagnostics(spc, methodList);

            var partialMethods = methodList.Where(m => m.IsPartialClass).ToList();
            if (partialMethods.Count == 0)
                return;

            var sourceText = SlashCommandEmitter.Emit(ns, partialMethods);
            spc.AddSource("SlashCommandHandlers.g.cs", SourceText.From(sourceText, Encoding.UTF8));
        });
    }

    private static CommandMethodModel? GetCommandMethodModel(GeneratorSyntaxContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
        if (methodSymbol is null) return null;

        var attr = methodSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is not null &&
            a.AttributeClass.ToDisplayString() == CommandAttributeFullName);
        if (attr is null) return null;

        var containingType = methodSymbol.ContainingType;
        if (containingType.TypeParameters.Length > 0 && containingType.TypeArguments.Length == 0)
            return null;   // 跳过开放泛型类型

        var fullyQualifiedFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        var commandName = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string ?? methodSymbol.Name
            : methodSymbol.Name;
        var description = attr.ConstructorArguments.Length > 1
            ? attr.ConstructorArguments[1].Value as string ?? ""
            : "";

        var aliases = new List<string>();
        foreach (var na in attr.NamedArguments)
        {
            if (na.Key == "Aliases")
                foreach (var v in na.Value.Values)
                    if (v.Value is string s) aliases.Add(s);
        }

        var classDecl = methodDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        var isPartialClass = classDecl is not null &&
                             classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

        return new CommandMethodModel
        {
            MethodName = methodSymbol.Name,
            ContainingTypeName = containingType.ToDisplayString(fullyQualifiedFormat),
            ContainingTypeShortName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            CommandName = commandName,
            Description = description,
            Aliases = aliases,
            IsPartialClass = isPartialClass,
        };
    }

    private static void ReportDiagnostics(SourceProductionContext spc, List<CommandMethodModel> methods)
    {
        // MIB020: 非 partial(每个类型只报一次)
        foreach (var group in methods.Where(m => !m.IsPartialClass).GroupBy(m => m.ContainingTypeName))
            spc.ReportDiagnostic(Diagnostic.Create(ClassNotPartial, null, group.First().ContainingTypeShortName));

        // MIB021: 空 description
        foreach (var m in methods.Where(m => string.IsNullOrWhiteSpace(m.Description)))
            spc.ReportDiagnostic(Diagnostic.Create(EmptyDescription, null, m.CommandName));

        // MIB022: 命令名/别名重复
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var m in methods)
        {
            foreach (var key in new[] { m.CommandName }.Concat(m.Aliases))
                if (!seen.Add(key))
                    spc.ReportDiagnostic(Diagnostic.Create(DuplicateCommand, null, key));
        }
    }
}
