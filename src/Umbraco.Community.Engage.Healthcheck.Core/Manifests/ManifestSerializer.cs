using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Core.Manifests;

public static class ManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Manifests are local files, never served as HTML: keep +, ', < and > readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmptyCollections } },
    };

    public static string Serialize(Manifest manifest) => JsonSerializer.Serialize(manifest, Options);

    public static Manifest Deserialize(string json)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(json, Options) ?? throw new InvalidDataException("Manifest file is empty.");
        if (manifest.FormatVersion > Manifest.CurrentFormatVersion)
            throw new InvalidDataException($"Manifest format {manifest.FormatVersion} is newer than this tool supports ({Manifest.CurrentFormatVersion}). Update the tool.");
        if (string.IsNullOrEmpty(manifest.Product) || string.IsNullOrEmpty(manifest.Version) || string.IsNullOrEmpty(manifest.StateKey))
            throw new InvalidDataException("Manifest is missing product, version or stateKey. Regenerate it with the generator.");
        if (manifest.Schema?.Tables is not { Count: > 0 })
            throw new InvalidDataException("Manifest has no tables. Regenerate it with the generator.");
        return manifest with { Schema = Normalize(manifest.Schema) };
    }

    private static void SkipEmptyCollections(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
            if (property.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                property.ShouldSerialize = (_, value) => value is IEnumerable e && e.GetEnumerator().MoveNext();
    }

    // Omitted lists come back as null from the record constructors; turn them back into empty lists.
    private static DatabaseSchema Normalize(DatabaseSchema? schema) => new((schema?.Tables ?? []).Select(t => t with
    {
        Columns = t.Columns ?? [],
        PrimaryKey = t.PrimaryKey is null ? null : t.PrimaryKey with { Columns = t.PrimaryKey.Columns ?? [] },
        UniqueConstraints = (t.UniqueConstraints ?? []).Select(u => u with { Columns = u.Columns ?? [] }).ToList(),
        ForeignKeys = (t.ForeignKeys ?? []).Select(f => f with { Columns = f.Columns ?? [], ReferencedColumns = f.ReferencedColumns ?? [] }).ToList(),
        Indexes = (t.Indexes ?? []).Select(i => i with { KeyColumns = i.KeyColumns ?? [], IncludedColumns = i.IncludedColumns ?? [] }).ToList(),
        CheckConstraints = t.CheckConstraints ?? [],
    }).ToList());
}
