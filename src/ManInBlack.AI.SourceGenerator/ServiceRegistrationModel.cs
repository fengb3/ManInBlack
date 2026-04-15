using Microsoft.CodeAnalysis;

namespace ManInBlack.AI.SourceGenerator;

public enum ServiceLifetime
{
    Transient,
    Scoped,
    Singleton
}

public sealed class ServiceRegistrationModel
{
    public ServiceLifetime Lifetime { get; set; }
    public string ImplementationType { get; set; } = "";
    public string? ServiceType { get; set; }
    public bool IsValidAssignment { get; set; }
    public Location? Location { get; set; }
}
