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
public sealed class ToolDeclarationGenerator : IIncrementalGenerator
{
    private const string ToolAttributeFullName = "ManInBlack.AI.Core.Attributes.AiToolAttribute";

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
        var toolMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetToolDeclarationModel(ctx))
            .Where(static m => m is not null)
            .Collect();

        context.RegisterSourceOutput(toolMethods, (spc, methods) =>
        {
            var methodList = methods.Where(m => m is not null).Select(m => m!).ToList();
            if (methodList.Count == 0)
                return;

            // 报告诊断
            ReportDiagnostics(spc, methodList);

            // 只为 partial 类生成代码
            var partialMethods = methodList.Where(m => m.IsPartialClass).ToList();

            if (partialMethods.Count == 0)
                return;

            // 解析命名冲突
            ResolveToolNames(partialMethods);

            // 按所属类型分组
            var groups = partialMethods.GroupBy(m => m.ContainingTypeName).ToList();

            foreach (var group in groups)
            {
                var first = group.First();
                var sourceText = ToolDeclarationEmitter.Emit(first.ContainingNamespace, first.ContainingTypeName, group.ToList());
                var fileName = first.ContainingTypeName.Replace('<', '_').Replace('>', '_').Replace('.', '_');
                spc.AddSource($"{fileName}.ToolDeclarations.g.cs", SourceText.From(sourceText, Encoding.UTF8));
            }
        });
    }

    private static ToolDeclarationModel? GetToolDeclarationModel(GeneratorSyntaxContext context)
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

        // 检查所属类是否为 partial
        var classDecl = methodDecl.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        var isPartialClass = classDecl is not null &&
                             classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
        var isStaticClass = classDecl is not null &&
                            classDecl.Modifiers.Any(SyntaxKind.StaticKeyword);
        var classLocation = classDecl?.Identifier.GetLocation();
        var methodLocation = methodDecl.Identifier.GetLocation();

        // 获取不含命名空间的类型名（仅类名+嵌套外层类名）
        var typeOnlyFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes);
        var typeNameWithoutNamespace = containingType.ToDisplayString(typeOnlyFormat);

        var parameters = methodSymbol.Parameters.Select(p => new ToolDeclarationParameterModel
        {
            Name = p.Name,
            Type = p.Type.ToDisplayString(fullyQualifiedFormat),
            IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated ||
                         p.Type.NullableAnnotation == NullableAnnotation.Annotated,
            HasDefaultValue = p.HasExplicitDefaultValue
        }).ToList();

        // 提取 XML 文档注释
        var (summary, paramDescriptions, returnsDescription) = ExtractXmlDoc(methodDecl);

        return new ToolDeclarationModel
        {
            MethodName = methodSymbol.Name,
            ContainingTypeName = typeNameWithoutNamespace,
            ContainingTypeShortName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ContainingNamespace = containingNamespace,
            IsStatic = methodSymbol.IsStatic,
            ReturnsVoid = methodSymbol.ReturnsVoid,
            ReturnType = methodSymbol.ReturnType.ToDisplayString(fullyQualifiedFormat),
            Parameters = parameters,
            Summary = summary,
            ParamDescriptions = paramDescriptions,
            ReturnsDescription = returnsDescription,
            IsPartialClass = isPartialClass,
            IsStaticClass = isStaticClass,
            ClassLocation = classLocation,
            MethodLocation = methodLocation
        };
    }

    private static (string? summary, Dictionary<string, string> paramDescriptions, string? returnsDescription)
        ExtractXmlDoc(MethodDeclarationSyntax methodDecl)
    {
        string? summary = null;
        var paramDescriptions = new Dictionary<string, string>();
        string? returnsDescription = null;

        // 尝试从结构化 trivia 中提取 XML 文档
        var docCommentTrivia = GetDocumentationCommentTrivia(methodDecl);

        if (docCommentTrivia is not null)
        {
            foreach (var node in docCommentTrivia.ChildNodes())
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

        // 回退方案：从原始 trivia 文本中手动解析 /// 行
        return ExtractXmlDocFromRawTrivia(methodDecl);
    }

    /// <summary>
    /// 从方法声明或其属性列表的 LeadingTrivia 中获取文档注释
    /// </summary>
    private static DocumentationCommentTriviaSyntax? GetDocumentationCommentTrivia(MethodDeclarationSyntax methodDecl)
    {
        // 先检查方法声明自身的 LeadingTrivia
        var docTrivia = methodDecl.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (docTrivia is not null)
            return docTrivia;

        // 如果方法有属性列表，检查第一个属性列表的 LeadingTrivia
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

    /// <summary>
    /// 从原始 trivia 文本中手动解析 /// 行（结构化 trivia 不可用时的回退方案）
    /// </summary>
    private static (string? summary, Dictionary<string, string> paramDescriptions, string? returnsDescription)
        ExtractXmlDocFromRawTrivia(MethodDeclarationSyntax methodDecl)
    {
        string? summary = null;
        var paramDescriptions = new Dictionary<string, string>();
        string? returnsDescription = null;

        // 收集所有 /// 行
        var docLines = new List<string>();
        CollectDocLines(methodDecl.GetLeadingTrivia(), docLines);

        if (docLines.Count == 0 && methodDecl.AttributeLists.Count > 0)
            CollectDocLines(methodDecl.AttributeLists[0].GetLeadingTrivia(), docLines);

        if (docLines.Count == 0)
            return (null, paramDescriptions, null);

        // 将 /// 行拼成 XML 并解析
        var xmlContent = string.Join("\n", docLines);
        return ParseXmlDocContent(xmlContent);
    }

    private static void CollectDocLines(SyntaxTriviaList triviaList, List<string> docLines)
    {
        foreach (var trivia in triviaList)
        {
            var text = trivia.ToString();
            // 文档注释 trivia 可能是多行的单一 trivia 条目，也可能每行一个 trivia
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
        string? summary = null;
        var paramDescriptions = new Dictionary<string, string>();
        string? returnsDescription = null;

        // 简单的 XML 解析：提取 <summary>, <param name="...">, <returns> 内容
        summary = ExtractXmlTagContent(xmlContent, "summary");
        returnsDescription = ExtractXmlTagContent(xmlContent, "returns");

        // 提取 <param> 标签（需要 name 属性）
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

    /// <summary>
    /// 从 XmlElement 内容中提取纯文本
    /// </summary>
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
            else if (node is XmlEmptyElementSyntax)
            {
                // 如 <paramref name="..."/> 等
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

    private static void ReportDiagnostics(SourceProductionContext spc, List<ToolDeclarationModel> methods)
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
                first.ClassLocation,
                first.ContainingTypeShortName));
        }

        foreach (var method in methods)
        {
            // MIB011: 缺少 summary
            if (string.IsNullOrWhiteSpace(method.Summary))
            {
                spc.ReportDiagnostic(Diagnostic.Create(MissingSummary, method.MethodLocation, method.MethodName));
            }

            // MIB012: 缺少 param 文档
            foreach (var param in method.Parameters)
            {
                if (!method.ParamDescriptions.ContainsKey(param.Name))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(MissingParamDoc, method.MethodLocation, method.MethodName, param.Name));
                }
            }

            // MIB013: 非 void 缺少 returns 文档
            if (!method.ReturnsVoid && string.IsNullOrWhiteSpace(method.ReturnsDescription))
            {
                spc.ReportDiagnostic(Diagnostic.Create(MissingReturnsDoc, method.MethodLocation, method.MethodName, method.ReturnType));
            }
        }
    }

    private static void ResolveToolNames(List<ToolDeclarationModel> methods)
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
}
