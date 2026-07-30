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

        var (summary, paramDescriptions, returnsDescription) = ExtractXmlDoc(methodDecl);

        var parameters = methodSymbol.Parameters.Select(p =>
        {
            var model = new ToolParameterModel
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
                    : null,
            };
            paramDescriptions.TryGetValue(p.Name, out var desc);
            model.JsonSchema = BuildJsonSchema(p.Type, desc, isUnsupported: out var unsupported, unsupportedReason: out var reason);
            model.IsUnsupportedType = unsupported;
            model.UnsupportedReason = reason;
            return model;
        }).ToList();

        // 检测 async 返回类型
        var (isAsync, actualReturnSymbol, actualReturnType, returnsVoid) = UnwrapAsyncReturnType(methodSymbol.ReturnType, fullyQualifiedFormat);

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

        // 提取 XML 文档注释（已在参数构造前完成）

        string? returnJsonSchema = null;
        if (!returnsVoid)
            returnJsonSchema = BuildJsonSchema(actualReturnSymbol, returnsDescription, out _, out _);

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
            ReturnJsonSchema = returnJsonSchema,
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

    #region 参数 JSON Schema 递归构造（生成器运行时拼接 JSON 字符串）

    private const int MaxSchemaDepth = 4;

    /// <summary>递归构造参数 JSON Schema 字符串。isUnsupported/unsupportedReason 由引用参数回传给调用方做诊断。</summary>
    private static string BuildJsonSchema(
        ITypeSymbol type, string? description,
        out bool isUnsupported, out string? unsupportedReason, int depth = 0)
    {
        isUnsupported = false;
        unsupportedReason = null;

        var (effective, isNullable) = UnwrapNullable(type);

        // 标量
        if (ScalarInfo(effective) is var (scalarType, format) && scalarType is not null)
            return ScalarJson(scalarType, format, isNullable, description);

        // enum
        if (effective.TypeKind == TypeKind.Enum)
            return EnumJson(effective, isNullable, description);

        // 数组 / 白名单集合（元素是否受支持由 CollectionJson 内的递归 BuildJsonSchema 回传）
        if (TryGetCollectionElement(effective) is { } elementType)
            return CollectionJson(elementType, isNullable, description, depth,
                out isUnsupported, out unsupportedReason);

        // 深度上限：降级为不透明 object
        if (depth >= MaxSchemaDepth)
            return OpaqueObjectJson(isNullable, description);

        // 受支持的对象（POCO / record）
        if (effective is INamedTypeSymbol named && IsSupportedObjectType(named))
            return ObjectJson(named, isNullable, description, depth);

        // 其余类型不支持
        isUnsupported = true;
        unsupportedReason = $"类型 '{effective.ToDisplayString()}' 不受支持";
        return OpaqueObjectJson(isNullable, description);
    }

    private static (ITypeSymbol effective, bool isNullable) UnwrapNullable(ITypeSymbol type)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol n && n.TypeArguments.Length == 1)
            return (n.TypeArguments[0], true);
        var isNullable = type.IsReferenceType &&
                         type.NullableAnnotation == NullableAnnotation.Annotated;
        return (isNullable ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) : type, isNullable);
    }

    /// <returns>(jsonType, format)；非标量返回 (null, null)。</returns>
    private static (string? type, string? format) ScalarInfo(ITypeSymbol t)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_Boolean: return ("boolean", null);
            case SpecialType.System_String:
            case SpecialType.System_Char: return ("string", null);
            case SpecialType.System_DateTime: return ("string", "date-time");
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal: return ("number", null);
            case SpecialType.System_Byte: case SpecialType.System_SByte:
            case SpecialType.System_Int16: case SpecialType.System_UInt16:
            case SpecialType.System_Int32: case SpecialType.System_UInt32:
            case SpecialType.System_Int64: case SpecialType.System_UInt64: return ("integer", null);
            default: break;
        }
        var fqn = t.ToDisplayString();
        if (fqn is "System.DateTimeOffset" or "DateTimeOffset") return ("string", "date-time");
        return (null, null);
    }

    private static string ScalarJson(string type, string? format, bool isNullable, string? description)
    {
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? $"\"type\":[\"{type}\",\"null\"]" : $"\"type\":\"{type}\"");
        if (format is not null) sb.Append($",\"format\":\"{format}\"");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string EnumJson(ITypeSymbol enumType, bool isNullable, string? description)
    {
        var names = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.ConstantValue is not null)
            .Select(f => $"\"{EscapeJson(f.Name)}\"");
        var values = string.Join(",", names);
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? $"\"type\":[\"string\",\"null\"]" : "\"type\":\"string\"");
        sb.Append($",\"enum\":[{values}]");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string OpaqueObjectJson(bool isNullable, string? description)
    {
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? "\"type\":[\"object\",\"null\"]" : "\"type\":\"object\"");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string CollectionJson(
        ITypeSymbol elementType, bool isNullable, string? description, int depth,
        out bool isUnsupported, out string? unsupportedReason)
    {
        var itemSchema = BuildJsonSchema(elementType, null, out isUnsupported, out unsupportedReason, depth + 1);
        var sb = new System.Text.StringBuilder("{");
        sb.Append(isNullable ? "\"type\":[\"array\",\"null\"]" : "\"type\":\"array\"");
        sb.Append($",\"items\":{itemSchema}");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string ObjectJson(INamedTypeSymbol type, bool isNullable, string? description, int depth)
    {
        var props = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.GetMethod is not null &&
                        p.GetMethod.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        var sb = new System.Text.StringBuilder("{\"type\":");
        sb.Append(isNullable ? "[\"object\",\"null\"]" : "\"object\"");
        sb.Append(",\"properties\":{");
        for (var i = 0; i < props.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = props[i];
            sb.Append($"\"{EscapeJson(ToCamelCase(p.Name))}\":");
            sb.Append(BuildJsonSchema(p.Type, null, out _, out _, depth + 1));
        }
        sb.Append('}');

        var required = props
            .Where(p => p.NullableAnnotation != NullableAnnotation.Annotated)
            .Select(p => $"\"{EscapeJson(ToCamelCase(p.Name))}\"").ToList();
        if (required.Count > 0)
        {
            sb.Append(",\"required\":[");
            sb.Append(string.Join(",", required));
            sb.Append(']');
        }
        if (!string.IsNullOrWhiteSpace(description)) sb.Append($",\"description\":\"{EscapeJson(description!)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>把 PascalCase 标识符转为 camelCase（首个 ASCII 大写字母小写化）。</summary>
    private static string ToCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsLower(s[0]))
            return s;
        if (s.Length == 1)
            return s.ToLowerInvariant();
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }

    private static ITypeSymbol? TryGetCollectionElement(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr) return arr.ElementType;
        if (type is INamedTypeSymbol named)
        {
            var def = named.ConstructedFrom.ToDisplayString();
            if (s_CollectionDefs.Contains(def) && named.TypeArguments.Length == 1)
                return named.TypeArguments[0];
        }
        return null;
    }

    private static readonly System.Collections.Generic.HashSet<string> s_CollectionDefs =
    [
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.HashSet<T>",
        "System.Collections.Generic.ISet<T>",
        "System.Collections.Generic.IReadOnlySet<T>",
        "System.Collections.Generic.Queue<T>",
        "System.Collections.Generic.Stack<T>",
        "System.Collections.Generic.LinkedList<T>",
    ];

    /// <summary>受支持的对象类型：非Dictionary、非tuple、非开放泛型的 class/struct。</summary>
    private static bool IsSupportedObjectType(INamedTypeSymbol t)
    {
        if (t.IsTupleType) return false;
        if (t.TypeArguments.Length > 0 && t.TypeParameters.Length > 0 &&
            t.TypeArguments.Any(a => a.Kind == SymbolKind.TypeParameter)) return false;
        var def = t.ConstructedFrom.ToDisplayString();
        if (def.StartsWith("System.Collections.Generic.Dictionary") ||
            def.StartsWith("System.Collections.Generic.IDictionary") ||
            def.StartsWith("System.Collections.Generic.IReadOnlyDictionary") ||
            def == "System.Object" || def == "object")
            return false;
        return t.TypeKind is TypeKind.Class or TypeKind.Struct;
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    #endregion

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

    private static (bool isAsync, ITypeSymbol returnTypeSymbol, string returnType, bool returnsVoid) UnwrapAsyncReturnType(
        ITypeSymbol returnType, SymbolDisplayFormat format)
    {
        if (returnType is not INamedTypeSymbol named)
            return (false, returnType, returnType.ToDisplayString(format), returnType.SpecialType == SpecialType.System_Void);

        if (!IsTaskType(named))
            return (false, returnType, returnType.ToDisplayString(format), returnType.SpecialType == SpecialType.System_Void);

        if (named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var innerType = named.TypeArguments[0];
            return (true, innerType, innerType.ToDisplayString(format), false);
        }

        return (true, returnType, "void", true);
    }

    private static bool IsTaskType(INamedTypeSymbol type)
    {
        var name = type.ConstructedFrom.Name;
        var ns = type.ConstructedFrom.ContainingNamespace?.ToDisplayString();
        return ns == "System.Threading.Tasks" && name is "Task" or "ValueTask";
    }
}
