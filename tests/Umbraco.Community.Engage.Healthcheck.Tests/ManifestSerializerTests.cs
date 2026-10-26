using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.State;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

public class ManifestSerializerTests
{
    private static Manifest Sample() => new(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, "{011f77fd-5e9d-4842-b960-9d0ff1a78b5a}", "Complete", SchemaBuilder.Baseline());

    [Fact]
    public void Round_trips_a_manifest()
    {
        var back = ManifestSerializer.Deserialize(ManifestSerializer.Serialize(Sample()));
        Assert.Equal("17.5.0", back.Version);
        Assert.Equal(3, back.Schema.Tables.Count);
        var child = back.Schema.Tables.Single(t => t.Name == "umbracoEngageChild");
        Assert.Equal("CASCADE", child.ForeignKeys.Single().DeleteAction);
        Assert.Equal(["contentTypeId"], child.Indexes.Single().IncludedColumns);
    }

    [Fact]
    public void Uses_camel_case_and_indents()
    {
        var json = ManifestSerializer.Serialize(Sample());
        Assert.Contains("\"formatVersion\": 1", json);
        Assert.Contains("\"version\": \"17.5.0\"", json);
    }

    [Fact]
    public void HostEngageVersion_is_written_when_set_and_omitted_when_null()
    {
        Assert.DoesNotContain("hostEngageVersion", ManifestSerializer.Serialize(Sample()));
        var json = ManifestSerializer.Serialize(Sample() with { HostEngageVersion = "17.5.0" });
        Assert.Contains("\"hostEngageVersion\": \"17.5.0\"", json);
        Assert.Equal("17.5.0", ManifestSerializer.Deserialize(json).HostEngageVersion);
        Assert.Null(ManifestSerializer.Deserialize(ManifestSerializer.Serialize(Sample())).HostEngageVersion);
    }

    [Fact]
    public void Refuses_a_newer_format()
    {
        var json = ManifestSerializer.Serialize(Sample() with { FormatVersion = 99 });
        Assert.Throws<InvalidDataException>(() => ManifestSerializer.Deserialize(json));
    }

    [Fact]
    public void Embedded_engage_manifests_load_from_gzip()
    {
        var store = new ManifestStore(null);
        Assert.Contains("17.5.0.fresh", store.Available(Products.Engage));
        var m = store.Find(Products.Engage, "17.5.0", "fresh")!;
        Assert.Equal(Manifest.CurrentFormatVersion, m.FormatVersion);
        Assert.Equal(86, m.Schema.Tables.Count);
    }

    [Fact]
    public void Embedded_resource_names_use_product_folders_and_gz()
    {
        var names = typeof(ManifestStore).Assembly.GetManifestResourceNames();
        Assert.Contains("manifests/engage/17.5.0.fresh.json.gz", names);
        Assert.Contains("manifests/engage/17.4.0.fresh.json.gz", names);
        Assert.All(names.Where(n => n.StartsWith("manifests/", StringComparison.Ordinal)), n =>
        {
            Assert.DoesNotContain("\\", n);
            Assert.EndsWith(".json.gz", n);
        });
    }

    [Fact]
    public void External_folder_uses_product_subfolders_and_wins()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var forms = Sample() with { Product = Products.Forms, Version = "99.1.2" };
            Directory.CreateDirectory(Path.Combine(dir, "forms"));
            File.WriteAllText(Path.Combine(dir, "forms", ManifestStore.FileName("99.1.2", "fresh")), ManifestSerializer.Serialize(forms));
            var store = new ManifestStore(dir);
            Assert.Equal("99.1.2", store.Find(Products.Forms, "99.1.2", "fresh")!.Version);
            Assert.Contains(store.All(Products.Forms), m => m.Version == "99.1.2");
            Assert.Contains("99.1.2.fresh", store.Available(Products.Forms));
            Assert.Null(store.Find(Products.Commerce, "99.0.0", "fresh"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void External_engage_manifest_wins_over_embedded_with_same_name()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "engage"));
            File.WriteAllText(Path.Combine(dir, "engage", ManifestStore.FileName("17.5.0", "fresh")), ManifestSerializer.Serialize(Sample()));
            var store = new ManifestStore(dir);
            Assert.Equal(3, store.Find(Products.Engage, "17.5.0", "fresh")!.Schema.Tables.Count);
            var all = store.All(Products.Engage);
            Assert.Equal(store.Available(Products.Engage).Count, all.Count);
            Assert.Equal(3, all.Single(m => m.Version == "17.5.0" && m.Variant == "fresh").Schema.Tables.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Relative_path_uses_product_folder()
    {
        Assert.Equal("forms/17.1.2.fresh.json", ManifestStore.RelativePath(Products.Forms, "17.1.2", "fresh"));
    }

    [Fact]
    public void Refuses_a_manifest_without_tables()
    {
        var json = ManifestSerializer.Serialize(Sample() with { Schema = new DatabaseSchema([]) });
        var ex = Assert.Throws<InvalidDataException>(() => ManifestSerializer.Deserialize(json));
        Assert.Equal("Manifest has no tables. Regenerate it with the generator.", ex.Message);
    }

    [Fact]
    public void Writer_does_not_escape_plus_quote_or_angle_brackets()
    {
        var json = ManifestSerializer.Serialize(Sample() with { SchemaStatus = "a+b'<c>" });
        Assert.Contains("\"a+b'<c>\"", json);
    }

    [Fact]
    public void Writer_omits_nulls_falses_and_empty_lists()
    {
        var json = ManifestSerializer.Serialize(Sample());
        Assert.DoesNotContain(": null", json);
        Assert.DoesNotContain(": false", json);
        Assert.DoesNotContain(": []", json);
    }

    [Fact]
    public void Trimmed_manifest_round_trips_with_defaults_restored()
    {
        var back = ManifestSerializer.Deserialize(ManifestSerializer.Serialize(Sample()));
        var parent = back.Schema.Tables.Single(t => t.Name == "umbracoEngageParent");
        Assert.Empty(parent.ForeignKeys);          // was omitted, must come back as [] not null
        Assert.Empty(parent.Indexes);
        Assert.Empty(parent.CheckConstraints);
        Assert.False(parent.Columns.Single(c => c.Name == "id").IsNullable);
        Assert.Null(parent.Columns.Single(c => c.Name == "id").Collation);
        var child = back.Schema.Tables.Single(t => t.Name == "umbracoEngageChild");
        Assert.Empty(child.UniqueConstraints);
        Assert.False(child.Indexes.Single().IsDisabled);
        Assert.Equal(Products.Engage, back.Product);
        Assert.Equal(MigrationState.StateKey, back.StateKey);
    }

    [Fact]
    public void Folder_maps_each_product()
    {
        Assert.Equal("engage", Products.Folder(Products.Engage));
        Assert.Equal("forms", Products.Folder(Products.Forms));
        Assert.Equal("commerce", Products.Folder(Products.Commerce));
        Assert.Throws<ArgumentOutOfRangeException>(() => Products.Folder("Nope"));
    }
}
