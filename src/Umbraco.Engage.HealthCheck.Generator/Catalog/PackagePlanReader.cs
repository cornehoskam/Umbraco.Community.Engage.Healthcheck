using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Umbraco.Engage.HealthCheck.Core;

namespace Umbraco.Engage.HealthCheck.Generator.Catalog;

/// <summary>One instruction of interest in a migration plan's constructor: a string literal load, or a call to a method.</summary>
public abstract record PlanInstruction
{
    public sealed record Literal(string Text) : PlanInstruction;
    public sealed record Call(string Method) : PlanInstruction;
}

/// <summary>
/// Reads a migration plan's last target state from the public NuGet package of a release (read-only): the constructor of
/// the type that derives from the CMS <c>MigrationPlan</c>, where each <c>To(...)</c> call takes its state as a string.
/// </summary>
public static class PackagePlanReader
{
    private const byte LdStr = 0x72;
    private const byte Call = 0x28;
    private const byte CallVirt = 0x6F;
    private const byte UserStringTable = 0x70;
    private const byte MethodDefTable = 0x06;
    private const byte MemberRefTable = 0x0A;
    private const byte MethodSpecTable = 0x2B;

    /// <summary>The NuGet package that holds a product's migration plan.</summary>
    public static string PackageId(string product)
        => product switch
        {
            Products.Engage => "umbraco.engage.web",
            Products.Forms => "umbraco.engage.forms",
            Products.Commerce => "umbraco.engage.commerce",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown product."),
        };

    /// <summary>The plan's last step for a release, from the package in <paramref name="nugetRoot"/>, or null with a reason.</summary>
    public static (string? Step, string? Problem) LastStep(string nugetRoot, Release release)
    {
        var lib = Path.Combine(nugetRoot, PackageId(release.Product), release.Version.ToLowerInvariant(), "lib");
        if (!Directory.Exists(lib)) return (null, $"package {PackageId(release.Product)} {release.Version} is not in {nugetRoot}");
        foreach (var dll in Directory.GetFiles(lib, "*.dll", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (PlanInstructions(dll) is { } instructions) return LastState(instructions);
        }
        return (null, $"no migration plan type in package {PackageId(release.Product)} {release.Version}");
    }

    /// <summary>
    /// Fails closed: the state of the last <c>To</c> call is the last string literal loaded since the call before it. A last
    /// call with no literal of its own (a field or a computed value) gives a reason instead of a guess.
    /// </summary>
    public static (string? Step, string? Problem) LastState(IEnumerable<PlanInstruction> instructions)
    {
        string? pending = null;
        string? state = null;
        var calls = 0;
        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case PlanInstruction.Literal literal:
                    pending = literal.Text;
                    break;
                case PlanInstruction.Call { Method: "To" }:
                    calls++;
                    state = pending;
                    pending = null;
                    break;
            }
        }
        if (calls == 0) return (null, "no To(...) step found in the plan");
        return state is null ? (null, "the last To(...) step does not take a string literal") : (state, null);
    }

    /// <summary>The literal loads and calls in the plan type's constructor, in order, or null when the assembly has no plan type.</summary>
    public static IReadOnlyList<PlanInstruction>? PlanInstructions(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return null;
        var reader = pe.GetMetadataReader();
        var plan = reader.TypeDefinitions.Select(reader.GetTypeDefinition).FirstOrDefault(t => BaseTypeName(reader, t) == "MigrationPlan");
        if (plan.Equals(default(TypeDefinition))) return null;

        var instructions = new List<PlanInstruction>();
        foreach (var method in plan.GetMethods().Select(reader.GetMethodDefinition))
        {
            if (reader.GetString(method.Name) != ".ctor" || method.RelativeVirtualAddress == 0) continue;
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            if (il is null) continue;
            // An operand is a 4-byte token whose high byte names its table. A stray match inside another operand would also
            // have to hit a valid string or method token, and the result is cross-checked against every generated manifest.
            for (var i = 0; i + 4 < il.Length; i++)
            {
                var row = il[i + 1] | il[i + 2] << 8 | il[i + 3] << 16;
                if (il[i] == LdStr && il[i + 4] == UserStringTable && TryUserString(reader, row) is { } text)
                {
                    instructions.Add(new PlanInstruction.Literal(text));
                    i += 4;
                }
                else if (il[i] is Call or CallVirt && MethodName(reader, il[i + 4], row) is { } name)
                {
                    instructions.Add(new PlanInstruction.Call(name));
                    i += 4;
                }
            }
        }
        return instructions;
    }

    private static string? BaseTypeName(MetadataReader reader, TypeDefinition type)
        => type.BaseType.IsNil ? null : type.BaseType.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)type.BaseType).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)type.BaseType).Name),
            _ => null,
        };

    private static string? TryUserString(MetadataReader reader, int offset)
    {
        try { return reader.GetUserString(MetadataTokens.UserStringHandle(offset)); }
        catch (BadImageFormatException) { return null; }
    }

    private static string? MethodName(MetadataReader reader, byte table, int row)
    {
        if (row == 0) return null;
        try
        {
            return table switch
            {
                MethodDefTable when row <= reader.MethodDefinitions.Count
                    => reader.GetString(reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row)).Name),
                MemberRefTable when row <= reader.MemberReferences.Count
                    => reader.GetString(reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row)).Name),
                MethodSpecTable when row <= reader.GetTableRowCount(TableIndex.MethodSpec)
                    => SpecName(reader, reader.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row)).Method),
                _ => null,
            };
        }
        catch (BadImageFormatException) { return null; }
    }

    private static string? SpecName(MetadataReader reader, EntityHandle method)
        => method.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)method).Name),
            HandleKind.MemberReference => reader.GetString(reader.GetMemberReference((MemberReferenceHandle)method).Name),
            _ => null,
        };
}
