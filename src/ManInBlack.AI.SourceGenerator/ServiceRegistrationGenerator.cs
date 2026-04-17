using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ManInBlack.AI.SourceGenerator;

[Generator]
public sealed class ServiceRegistrationGenerator : IIncrementalGenerator
{
    private const string TransientAttributeFullName = "ManInBlack.AI.Core.Attributes.ServiceRegister.TransientAttribute";
    private const string TransientAsAttributePrefix = "ManInBlack.AI.Core.Attributes.ServiceRegister.Transient.AsAttribute";
    private const string ScopedAttributeFullName = "ManInBlack.AI.Core.Attributes.ServiceRegister.ScopedAttribute";
    private const string ScopedAsAttributePrefix = "ManInBlack.AI.Core.Attributes.ServiceRegister.Scoped.AsAttribute";
    private const string SingletonAttributeFullName = "ManInBlack.AI.Core.Attributes.ServiceRegister.SingletonAttribute";
    private const string SingletonAsAttributePrefix = "ManInBlack.AI.Core.Attributes.ServiceRegister.Singleton.AsAttribute";

    private static readonly DiagnosticDescriptor ServiceTypeNotAssignable = new(
        id: "MIB001",
        title: "Service type is not assignable from implementation type",
        messageFormat: "Type '{0}' does not implement or inherit from '{1}'",
        category: "ServiceRegister",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The implementation type must be assignable to the service type specified in [ServiceRegister.X.As<T>].");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var registrations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetServiceRegistrationModel(ctx))
            .Where(static m => m is not null)
            .Collect();

        var rootNamespaces = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns) ? ns : "");

        var registrationsWithNamespace = registrations.Combine(rootNamespaces);

        context.RegisterSourceOutput(registrationsWithNamespace, (spc, pair) =>
        {
            var models = pair.Left;
            var rootNamespace = pair.Right;

            var modelList = models.Where(m => m is not null).Select(m => m!).ToList();

            foreach (var model in modelList.Where(m => !m.IsValidAssignment))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    ServiceTypeNotAssignable,
                    model.Location,
                    model.ImplementationType,
                    model.ServiceType));
            }

            var validModels = modelList.Where(m => m.IsValidAssignment).ToList();

            if (validModels.Count == 0)
                return;

            var sourceText = ServiceRegistrationEmitter.Emit(validModels, rootNamespace);
            spc.AddSource("ServiceRegistrationExtensions.g.cs", SourceText.From(sourceText, Encoding.UTF8));
        });
    }

    private static ServiceRegistrationModel? GetServiceRegistrationModel(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var typeSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;

        if (typeSymbol is null)
            return null;

        if (typeSymbol.IsAbstract || typeSymbol.IsStatic)
            return null;

        if (typeSymbol.TypeParameters.Length > 0 && typeSymbol.TypeArguments.Length == 0)
            return null;

        var fullyQualifiedFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

        ServiceLifetime? lifetime = null;
        ITypeSymbol? serviceTypeSymbol = null;

        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass is null)
                continue;

            var attrName = attr.AttributeClass.ToDisplayString(fullyQualifiedFormat);

            if (attrName == TransientAttributeFullName)
            {
                lifetime = ServiceLifetime.Transient;
            }
            else if (attrName.StartsWith(TransientAsAttributePrefix))
            {
                lifetime = ServiceLifetime.Transient;
                serviceTypeSymbol = attr.AttributeClass.TypeArguments.FirstOrDefault();
            }
            else if (attrName == ScopedAttributeFullName)
            {
                lifetime = ServiceLifetime.Scoped;
            }
            else if (attrName.StartsWith(ScopedAsAttributePrefix))
            {
                lifetime = ServiceLifetime.Scoped;
                serviceTypeSymbol = attr.AttributeClass.TypeArguments.FirstOrDefault();
            }
            else if (attrName == SingletonAttributeFullName)
            {
                lifetime = ServiceLifetime.Singleton;
            }
            else if (attrName.StartsWith(SingletonAsAttributePrefix))
            {
                lifetime = ServiceLifetime.Singleton;
                serviceTypeSymbol = attr.AttributeClass.TypeArguments.FirstOrDefault();
            }
        }

        if (lifetime is null)
            return null;

        var model = new ServiceRegistrationModel
        {
            Lifetime = lifetime.Value,
            ImplementationType = typeSymbol.ToDisplayString(fullyQualifiedFormat),
            Location = classDecl.Identifier.GetLocation()
        };

        if (serviceTypeSymbol is not null)
        {
            model.ServiceType = serviceTypeSymbol.ToDisplayString(fullyQualifiedFormat);

            var csharpCompilation = (CSharpCompilation)semanticModel.Compilation;
            var conversion = csharpCompilation.ClassifyConversion(typeSymbol, serviceTypeSymbol);
            model.IsValidAssignment = conversion.Exists &&
                                      (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing);
        }
        else
        {
            model.IsValidAssignment = true;
        }

        return model;
    }
}
