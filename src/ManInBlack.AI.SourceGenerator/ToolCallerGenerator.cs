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
    private const string ToolAttributeFullName = "ManInBlack.AI.Abstraction.Attributes.AiToolAttribute";
    private const string HasFilterAttributePrefix = "ManInBlack.AI.Abstraction.Attributes.AiTool.HasFilterAttribute";

    private static readonly DiagnosticDescriptor ClassNotPartial = new(
        id: "MIB010",
        title: "包含 [AiTool] 方法的类必须声明为 partial",
        messageFormat: "类 '{0}' 包含 [AiTool] 方法，必须声明为 partial",
        category: "AiToolDeclaration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingSummary = new(
        id: "MIB011",
        title: "[AiTool] 方法缺少 <summary> XML 文档",
        messageFormat: "[AiTool] 方法 '{0}' 缺少 <summary> XML 文档注释",
        category: "AiToolDeclaration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingParamDoc = new(
        id: "MIB012",
        title: "[AiTool] 方法参数缺少 <param> XML 文档",
        messageFormat: "[AiTool] 方法 '{0}' 的参数 '{1}' 缺少 <param> XML 文档注释",
        category: "AiToolDeclaration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingReturnsDoc = new(
        id: "MIB013",
        title: "[AiTool] 方法缺少 <returns> XML 文档",
        messageFormat: "[AiTool] 方法 '{0}' 返回值类型为 '{1}'，但缺少 <returns> XML 文档注释",
        category: "AiToolDeclaration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

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

            // 报告诊断
            ReportDiagnostics(spc, methodList);

            // 只为 partial 类生成代码
            var partialMethods = methodList.Where(m => m.IsPartialClass).ToList();
            if (partialMethods.Count == 0)
                return;

            // 解析命名冲突：同名方法加类名前缀
            ResolveToolNames(partialMethods);

            var sourceText = ToolCallerEmitter.Emit(ns, partialMethods);
            spc.AddSource("ToolHandlers.g.cs", SourceText.From(sourceText, Encoding.UTF8));
        });
    }

    private static ToolMethodModel? GetToolMethodModel(GeneratorSyntaxContext context)
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

        var containingNamespace = containingType.ContainingNamespace.ToDisplayString(
            new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));

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

        // 检测 async 返回类型
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

        // 检查所属类是否为 partial / static
        var classDecl = methodDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        var isPartialClass = classDecl is not null &&
                             classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
        var isStaticClass = classDecl is not null &&
                            classDecl.Modifiers.Any(SyntaxKind.StaticKeyword);

        // 提取 XML 文档注释
        var (summary, paramDescriptions, returnsDescription) = ExtractXmlDoc(methodDecl);

        return new ToolMethodModel
        {
            MethodName = methodSymbol.Name,
            ContainingTypeName = containingType.ToDisplayString(fullyQualifiedFormat),
            ContainingTypeShortName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            FullyQualifiedTypeName = containingType.ToDisplayString(fullyQualifiedFormat),
            ContainingNamespace = containingNamespace,
            IsStatic = methodSymbol.IsStatic,
            IsAsync = isAsync,
            ReturnsVoid = returnsVoid,
            ReturnType = actualReturnType,
            Parameters = parameters,
            FilterTypes = filterTypes,
            Summary = summary,
            ParamDescriptions = paramDescriptions,
            ReturnsDescription = returnsDescription,
            IsPartialClass = isPartialClass,
            IsStaticClass = isStaticClass,
        };
    }

    private static void ReportDiagnostics(SourceProductionContext spc, List<ToolMethodModel> methods)
    {
        // MIB010: 非 partial 类（每个类型只报一次）
        var nonPartialTypes = methods
            .Where(m => !m.IsPartialClass)
            .GroupBy(m => m.ContainingTypeName);

        foreach (var group in nonPartialTypes)
        {
            var first = group.First();
            spc.ReportDiagnostic(Diagnostic.Create(
                ClassNotPartial,
                null,
                first.ContainingTypeShortName));
        }

        foreach (var method in methods)
        {
            // MIB011: 缺少 summary
            if (string.IsNullOrWhiteSpace(method.Summary))
            {
                spc.ReportDiagnostic(Diagnostic.Create(MissingSummary, null, method.MethodName));
            }

            // MIB012: 缺少 param 文档
            foreach (var param in method.Parameters)
            {
                if (!method.ParamDescriptions.ContainsKey(param.Name))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(MissingParamDoc, null, method.MethodName, param.Name));
                }
            }

            // MIB013: 非 void 缺少 returns 文档
            if (!method.ReturnsVoid && string.IsNullOrWhiteSpace(method.ReturnsDescription))
            {
                spc.ReportDiagnostic(Diagnostic.Create(MissingReturnsDoc, null, method.MethodName, method.ReturnType));
            }
        }
    }

    #region XML 文档提取

    private static (string? summary, Dictionary<string, string> paramDescriptions, string? returnsDescription)
        ExtractXmlDoc(MethodDeclarationSyntax methodDecl)
    {
        var docCommentTrivia = GetDocumentationCommentTrivia(methodDecl);

        if (docCommentTrivia is not null)
        {
            return ExtractFromStructuredTrivia(docCommentTrivia);
        }

        return ExtractFromRawTrivia(methodDecl);
    }

    private static DocumentationCommentTriviaSyntax? GetDocumentationCommentTrivia(MethodDeclarationSyntax methodDecl)
    {
        var docTrivia = methodDecl.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (docTrivia is not null)
            return docTrivia;

        if (methodDecl.AttributeLists.Count > 0)
        {
            docTrivia = methodDecl.AttributeLists[0].GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                .Select(t => t.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();
        }

        return docTrivia;
    }

    private static (string? summary, Dictionary<string, string> paramDescriptions, string? returnsDescription)
        ExtractFromStructuredTrivia(DocumentationCommentTriviaSyntax docComment)
    {
        string? summary = null;
        var paramDescriptions = new Dictionary<string, string>();
        string? returnsDescription = null;

        foreach (var node in docComment.ChildNodes())
        {
            if (node is XmlElementSyntax xmlElement)
            {
                var tagName = xmlElement.StartTag.Name.ToString().Trim();
                var contentText = GetXmlTextContent(xmlElement.Content);

                switch (tagName)
                {
                    case "summary":
                        summary = contentText;
                        break;
                    case "returns":
                        returnsDescription = contentText;
                        break;
                    case "param":
                        var nameAttr = xmlElement.StartTag.Attributes
                            .OfType<XmlNameAttributeSyntax>()
                            .FirstOrDefault();
                        if (nameAttr is not null)
                        {
                            var paramName = nameAttr.Identifier.ToString();
                            paramDescriptions[paramName] = contentText;
                        }
                        break;
                }
            }
        }

        return (summary, paramDescriptions, returnsDescription);
    }

    private static string GetXmlTextContent(SyntaxList<XmlNodeSyntax> content)
    {
        var parts = new List<string>();
        foreach (var node in content)
        {
            if (node is XmlTextSyntax textNode)
            {
                foreach (var token in textNode.TextTokens)
                {
                    var text = token.ValueText.Trim();
                    if (!string.IsNullOrEmpty(text))
                        parts.Add(text);
                }
            }
            else if (node is XmlElementSyntax nestedElement)
            {
                var nestedText = GetXmlTextContent(nestedElement.Content);
                if (!string.IsNullOrEmpty(nestedText))
                    parts.Add(nestedText);
            }
        }
        return string.Join(" ", parts).Trim();
    }

    private static (string? summary, Dictionary<string, string> paramDescriptions, string? returnsDescription)
        ExtractFromRawTrivia(MethodDeclarationSyntax methodDecl)
    {
        var paramDescriptions = new Dictionary<string, string>();
        var docLines = new List<string>();
        CollectDocLines(methodDecl.GetLeadingTrivia(), docLines);

        if (docLines.Count == 0 && methodDecl.AttributeLists.Count > 0)
            CollectDocLines(methodDecl.AttributeLists[0].GetLeadingTrivia(), docLines);

        if (docLines.Count == 0)
            return (null, paramDescriptions, null);

        var xmlContent = string.Join("\n", docLines);
        return ParseXmlDocContent(xmlContent);
    }

    private static void CollectDocLines(SyntaxTriviaList triviaList, List<string> docLines)
    {
        foreach (var trivia in triviaList)
        {
            var text = trivia.ToString();
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("///"))
                    docLines.Add(trimmed.Substring(3));
            }
        }
    }

    private static (string? summary, Dictionary<string, string> paramDescriptions, string? returnsDescription)
        ParseXmlDocContent(string xmlContent)
    {
        string? summary = ExtractXmlTagContent(xmlContent, "summary");
        string? returnsDescription = ExtractXmlTagContent(xmlContent, "returns");
        var paramDescriptions = new Dictionary<string, string>();

        var paramPattern = "<param name=\"";
        var idx = 0;
        while ((idx = xmlContent.IndexOf(paramPattern, idx)) >= 0)
        {
            idx += paramPattern.Length;
            var nameEnd = xmlContent.IndexOf('"', idx);
            if (nameEnd < 0) continue;
            var paramName = xmlContent.Substring(idx, nameEnd - idx);

            var contentStart = xmlContent.IndexOf('>', nameEnd);
            if (contentStart < 0) continue;
            contentStart++;

            var contentEnd = xmlContent.IndexOf("</param>", contentStart);
            if (contentEnd < 0) continue;

            var content = xmlContent.Substring(contentStart, contentEnd - contentStart).Trim();
            paramDescriptions[paramName] = content;
        }

        return (summary, paramDescriptions, returnsDescription);
    }

    private static string? ExtractXmlTagContent(string xml, string tagName)
    {
        var startTag = $"<{tagName}>";
        var endTag = $"</{tagName}>";
        var startIdx = xml.IndexOf(startTag);
        if (startIdx < 0) return null;
        startIdx += startTag.Length;
        var endIdx = xml.IndexOf(endTag, startIdx);
        if (endIdx < 0) return null;
        return xml.Substring(startIdx, endIdx - startIdx).Trim();
    }

    #endregion

    private static string? FormatDefaultValue(object? value, ITypeSymbol type)
    {
        if (value is null) return "null";
        if (value is bool b) return b ? "true" : "false";
        if (value is string s) return $"\"{s}\"";
        if (value is char c) return $"'{c}'";
        if (value.GetType().IsEnum) return $"{type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{value}";
        return value.ToString();
    }

    private static void ResolveToolNames(List<ToolMethodModel> methods)
    {
        var groups = methods.GroupBy(m => m.MethodName).ToList();

        foreach (var group in groups)
        {
            if (group.Count() > 1)
            {
                foreach (var method in group)
                    method.ToolName = $"{method.ContainingTypeShortName}.{method.MethodName}";
            }
            else
            {
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

        if (named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var innerType = named.TypeArguments[0];
            return (true, innerType.ToDisplayString(format), false);
        }

        return (true, "void", true);
    }

    private static bool IsTaskType(INamedTypeSymbol type)
    {
        var name = type.ConstructedFrom.Name;
        var ns = type.ConstructedFrom.ContainingNamespace?.ToDisplayString();
        return ns == "System.Threading.Tasks" && name is "Task" or "ValueTask";
    }
}
