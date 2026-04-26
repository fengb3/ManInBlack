namespace Bwarp.Security;

public enum CapabilityActionKind { Add, Drop }

public readonly record struct CapabilityAction(CapabilityActionKind Kind, string Capability)
{
    public static CapabilityAction Add(string cap) => new(CapabilityActionKind.Add, cap);
    public static CapabilityAction Drop(string cap) => new(CapabilityActionKind.Drop, cap);
    public static CapabilityAction AddAll() => new(CapabilityActionKind.Add, "ALL");
    public static CapabilityAction DropAll() => new(CapabilityActionKind.Drop, "ALL");
}
