using System.CommandLine;
using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.State;
using Umbraco.Community.Engage.Healthcheck.Generator;
using Umbraco.Community.Engage.Healthcheck.Generator.Catalog;
using Umbraco.Community.Engage.Healthcheck.Generator.Verify;

var engageVersion = new Option<string>("--engage-version") { Required = true };
var umbracoVersion = new Option<string?>("--umbraco-version") { Description = "CMS version for a fresh install. Not used by --variant aligned or complete (the catalog has it)." };
var host = new Option<string?>("--host") { Description = "Path to the host project folder, e.g. hosts/Host.V17. Not used by --variant aligned or complete (the catalog has it)." };
var forms = new Option<string?>("--forms") { Description = "Engage Forms version to install. Writes the Forms manifest instead of the Engage one." };
var umbracoForms = new Option<string>("--umbraco-forms") { DefaultValueFactory = _ => "17.6.1", Description = "Umbraco Forms version to install with --forms." };
var commerce = new Option<string?>("--commerce") { Description = "Engage Commerce version to install. Writes the Commerce manifest instead of the Engage one." };
var umbracoCommerce = new Option<string>("--umbraco-commerce") { DefaultValueFactory = _ => "17.2.8", Description = "Umbraco Commerce version to install with --commerce." };
var output = new Option<string>("--out") { DefaultValueFactory = _ => "manifests" };
var variant = new Option<string>("--variant")
{
    DefaultValueFactory = _ => Variants.Fresh,
    Description = "fresh: a clean install. aligned: install the catalog's alignFrom release (or --from), upgrade to the release, snapshot with status Aligned. complete: like aligned, then EnsureDataConsistency.sql and CompleteAlignSchema.sql from --align-scripts, snapshot with status Complete.",
};
variant.AcceptOnlyFromAmong(Variants.Fresh, Variants.Aligned, Variants.Complete);
var from = new Option<string?>("--from") { Description = "The older Engage release to install first. Default for --variant aligned or complete: the catalog's alignFrom." };
var catalogPath = new Option<string>("--catalog") { DefaultValueFactory = _ => "catalog/releases.json", Description = "The release catalog. Host paths in it resolve against the folder above the catalog folder." };
var alignScripts = new Option<string?>("--align-scripts") { Description = "Folder with CompleteAlignSchema.sql and EnsureDataConsistency.sql, downloaded from the Engage schema alignment guide on docs.umbraco.com. A V13, V16, V17 or V18 subfolder is used first when it exists. Default: the ENGAGE_ALIGN_SCRIPTS environment variable." };
var verifyAligned = new Option<bool>("--verify") { Description = "With --variant aligned: after the snapshot, run CompleteAlignSchema.sql from --align-scripts on the same database and compare with the fresh manifest." };
var nugetRoot = new Option<string>("--nuget")
{
    DefaultValueFactory = _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
    Description = "The NuGet package cache (read-only): the Engage packages for the last-step check, umbraco.cms.infrastructure for cms-states.",
};

var generate = new Command("generate", "Install one Engage version (and optional add-ons) and write its manifest(s): a clean install, or with --variant aligned an upgrade from an older release.")
{
    engageVersion, umbracoVersion, host, forms, umbracoForms, commerce, umbracoCommerce, output, variant, from, catalogPath, alignScripts, verifyAligned,
};

generate.SetAction(async (parse, ct) =>
{
    var variantName = parse.GetValue(variant)!;
    if (Variants.IsAligned(variantName) || Variants.IsComplete(variantName))
    {
        var completeVariant = Variants.IsComplete(variantName);
        if (completeVariant && parse.GetValue(verifyAligned)) return UsageError("--verify is for --variant aligned.");
        if (Trimmed(parse.GetValue(forms)) is not null || Trimmed(parse.GetValue(commerce)) is not null)
            return UsageError("Add-ons have only the fresh variant.");
        if (LoadCatalog(parse.GetValue(catalogPath)!) is not ({ } catalog, { } root)) return 2;
        var release = catalog.Find(Products.Engage, parse.GetValue(engageVersion)!);
        if (release is null) return UsageError($"Engage {parse.GetValue(engageVersion)} is not in the catalog.");
        var fromVersion = Trimmed(parse.GetValue(from)) ?? release.AlignFrom;
        if (fromVersion is null) return UsageError($"{release} has no alignFrom in the catalog; give --from.");

        string? scriptsFolder = null;
        if ((completeVariant || parse.GetValue(verifyAligned)) && (scriptsFolder = ScriptsFolder(parse.GetValue(alignScripts))) is null) return 3;
        var verdicts = new List<(string Version, AssumptionVerdict Verdict)>();
        var hook = parse.GetValue(verifyAligned) ? VerifyHook(release, scriptsFolder!, parse.GetValue(output)!, KnownVariants.LoadNextTo(parse.GetValue(catalogPath)!), verdicts) : null;
        var manifests = await ManifestGenerator.GenerateAsync(catalog.StagesFrom(fromVersion, release, root), fromVersion, variantName, hook, ct,
            completeAlignment: completeVariant ? CompleteAlignment(release, scriptsFolder!) : null);
        foreach (var manifest in manifests) await WriteAndPrint(manifest, parse.GetValue(output)!, ct);
        return verdicts.Any(v => v.Verdict.ExitCode == AssumptionVerdict.ExitFail) ? AssumptionVerdict.ExitFail : verdicts.Any(v => v.Verdict.IsKnownVariant) ? AssumptionVerdict.ExitKnownVariant : 0;
    }

    var formsVersion = Trimmed(parse.GetValue(forms));
    var commerceVersion = Trimmed(parse.GetValue(commerce));
    if (parse.GetValue(host) is not { } hostPath || parse.GetValue(umbracoVersion) is not { } cms)
        return UsageError("A fresh generate needs --host and --umbraco-version.");
    if (Trimmed(parse.GetValue(from)) is not null) return UsageError("--from is for --variant aligned or complete; a staged fresh install comes from the catalog's installFrom.");
    if (formsVersion is not null && commerceVersion is not null)
        return UsageError("Generate one add-on at a time: combination manifests are never needed.");
    if (formsVersion is null && parse.GetResult(umbracoForms) is { Implicit: false })
        return UsageError("--umbraco-forms needs --forms: it only sets the Umbraco Forms version installed alongside Engage Forms.");
    if (commerceVersion is null && parse.GetResult(umbracoCommerce) is { Implicit: false })
        return UsageError("--umbraco-commerce needs --commerce: it only sets the Umbraco Commerce version installed alongside Engage Commerce.");

    var spec = new HostSpec(
        Path.GetFullPath(hostPath), parse.GetValue(engageVersion)!, cms,
        formsVersion, formsVersion is null ? null : parse.GetValue(umbracoForms),
        commerceVersion, commerceVersion is null ? null : parse.GetValue(umbracoCommerce));
    foreach (var manifest in await ManifestGenerator.GenerateAsync(spec, ct)) await WriteAndPrint(manifest, parse.GetValue(output)!, ct);
    return 0;
});

var product = new Option<string?>("--product") { Description = "Only this product: Engage, Engage.Forms or Engage.Commerce." };
var major = new Option<string?>("--major") { Description = "Only this major: V13, V16, V17 or V18." };
var force = new Option<bool>("--force") { Description = "Generate again even when the manifest exists." };
var generateAll = new Command("generate-all", "Generate every catalog release that has no manifest yet, one at a time, and check each manifest's last step against the plan in the release's NuGet package.")
{
    catalogPath, product, major, output, force, nugetRoot, alignScripts, variant, verifyAligned,
};

generateAll.SetAction(async (parse, ct) =>
{
    if (LoadCatalog(parse.GetValue(catalogPath)!) is not ({ } catalog, { } root)) return 2;
    var releases = catalog.Select(parse.GetValue(product), parse.GetValue(major));
    if (releases.Count == 0) return UsageError("No catalog releases match --product/--major.");
    var outFolder = parse.GetValue(output)!;
    var variantName = parse.GetValue(variant)!;
    var verify = parse.GetValue(verifyAligned);
    if (verify && !Variants.IsAligned(variantName)) return UsageError("--verify needs --variant aligned.");
    string? scriptsFolder = null;
    if ((verify || Variants.IsComplete(variantName)) && (scriptsFolder = ScriptsFolder(parse.GetValue(alignScripts))) is null) return 3;
    var nuget = parse.GetValue(nugetRoot)!;
    var known = KnownVariants.LoadNextTo(parse.GetValue(catalogPath)!);

    var verdicts = new List<(string Version, AssumptionVerdict Verdict)>();
    var results = await GenerateAllRunner.RunAsync(
        catalog, releases, outFolder, parse.GetValue(force), root,
        (stages, installedFrom, token) =>
        {
            var release = catalog.Find(Products.Engage, stages[^1].EngageVersion);
            var engageOnly = release is not null && stages[^1].FormsVersion is null && stages[^1].CommerceVersion is null;
            var hook = verify && engageOnly ? VerifyHook(release!, scriptsFolder!, outFolder, known, verdicts) : null;
            var complete = Variants.IsComplete(variantName) && engageOnly ? CompleteAlignment(release!, scriptsFolder!) : null;
            return ManifestGenerator.GenerateAsync(stages, installedFrom, variantName, hook, token, completeAlignment: complete);
        },
        (m, r) => LastStepCheck.Check(m, r, nuget), Console.Out, ct, variantName);
    Console.WriteLine(GenerateAllRunner.Summary(results));
    if (verify)
    {
        Console.WriteLine("Verify (CompleteAlignSchema.sql, then compare with the complete manifest, or fresh when there is none):");
        foreach (var (version, verdict) in verdicts) verdict.WriteTo(Console.Out, $"  {version}");
    }
    // A known, recorded variant is not a run failure; an unrecorded difference or an irreproducible complete manifest is.
    return GenerateAllRunner.HasFailures(results) || verdicts.Any(v => v.Verdict.ExitCode == AssumptionVerdict.ExitFail) ? 1 : 0;
});

var version = new Option<string>("--version") { Required = true, Description = "The Engage release to verify." };
var mode = new Option<string>("--mode")
{
    DefaultValueFactory = _ => "aligned",
    Description = "aligned: upgrade from --from (default the catalog's alignFrom), run CompleteAlignSchema.sql, compare with the complete manifest (fresh when there is none; the fresh difference count is shown either way). upgrade: upgrade from --from and compare with fresh, no align script.",
};
mode.AcceptOnlyFromAmong("aligned", "upgrade");
var manifestsFolder = new Option<string>("--manifests") { DefaultValueFactory = _ => "manifests", Description = "Folder with the fresh, aligned and complete manifests to compare with." };
var verifyCommand = new Command("verify", "Check the 'completed = fresh' (aligned mode) or 'upgraded = fresh' (upgrade mode) assumption for one release on a real database.")
{
    version, from, mode, catalogPath, alignScripts, manifestsFolder,
};

verifyCommand.SetAction(async (parse, ct) =>
{
    if (LoadCatalog(parse.GetValue(catalogPath)!) is not ({ } catalog, { } root)) return 2;
    var release = catalog.Find(Products.Engage, parse.GetValue(version)!);
    if (release is null) return UsageError($"Engage {parse.GetValue(version)} is not in the catalog.");
    var store = new ManifestStore(parse.GetValue(manifestsFolder)!, includeEmbedded: false);
    var fresh = store.Find(Products.Engage, release.Version, Variants.Fresh);
    if (fresh is null) return UsageError($"No fresh manifest for Engage {release.Version} in {parse.GetValue(manifestsFolder)}.");
    var upgrade = parse.GetValue(mode) == "upgrade";
    var fromVersion = Trimmed(parse.GetValue(from)) ?? (upgrade ? null : release.AlignFrom);
    if (fromVersion is null) return UsageError(upgrade ? "--mode upgrade needs --from." : $"{release} has no alignFrom in the catalog; give --from.");
    var stages = catalog.StagesFrom(fromVersion, release, root);
    var known = KnownVariants.LoadNextTo(parse.GetValue(catalogPath)!);

    if (upgrade)
    {
        var upgraded = (await ManifestGenerator.GenerateAsync(stages, fromVersion, Variants.Fresh, null, ct, databaseName: "verify")).Single();
        var verdict = Verifier.Judge(upgraded.Schema, new MigrationState(true, upgraded.LastMigrationStep, upgraded.SchemaStatus), fresh, null,
            KnownVariants.Find(known, Products.Engage, release.Version, KnownVariants.UpgradedEqualsFresh), expectStatus: null);
        verdict.WriteTo(Console.Out, $"verify upgrade {fromVersion} -> {release.Version} (status fresh {fresh.SchemaStatus ?? "(none)"}, upgraded {upgraded.SchemaStatus ?? "(none)"})");
        return verdict.ExitCode;
    }

    if (ScriptsFolder(parse.GetValue(alignScripts)) is not { } scriptsFolder) return 3;
    var verdicts = new List<(string Version, AssumptionVerdict Verdict)>();
    var aligned = (await ManifestGenerator.GenerateAsync(stages, fromVersion, Variants.Aligned,
        VerifyHook(release, scriptsFolder, parse.GetValue(manifestsFolder)!, known, verdicts), ct, databaseName: "verify")).Single();
    if (store.Find(Products.Engage, release.Version, Variants.Aligned) is { } stored)
    {
        var same = SchemaComparer.Compare(stored.Schema, aligned.Schema).Count == 0
                   && SchemaComparer.Compare(aligned.Schema, stored.Schema).Count == 0
                   && stored.LastMigrationStep == aligned.LastMigrationStep;
        Console.WriteLine($"Aligned snapshot {(same ? "equals" : "DIFFERS FROM")} the stored aligned manifest.");
    }
    return verdicts.Count == 1 ? verdicts[0].Verdict.ExitCode : AssumptionVerdict.ExitFail;
});

var statesOut = new Option<string>("--out") { DefaultValueFactory = _ => "src/Umbraco.Community.Engage.Healthcheck.Core/Data/cms-states.json" };
var cmsStatesCommand = new Command("cms-states", "Extract the CMS upgrade states of every catalog host's CMS version into the embedded cms-states.json.")
{
    catalogPath, nugetRoot, statesOut,
};

cmsStatesCommand.SetAction(async (parse, ct) =>
{
    if (LoadCatalog(parse.GetValue(catalogPath)!) is not ({ } catalog, _)) return 2;
    var data = CmsStateExtractor.Build(catalog.Hosts, parse.GetValue(nugetRoot)!);
    // A host whose own final CMS state maps to another major would make detection name the wrong major: write nothing.
    var problems = CmsStateExtractor.CheckHostsMapToOwnMajor(catalog.Hosts, parse.GetValue(nugetRoot)!, data);
    if (problems.Count > 0)
    {
        foreach (var problem in problems) Console.Error.WriteLine(problem);
        Console.Error.WriteLine("cms-states.json was not written.");
        return 1;
    }
    var path = Path.GetFullPath(parse.GetValue(statesOut)!);
    await File.WriteAllTextAsync(path, CmsStates.Serialize(data) + Environment.NewLine, ct);
    foreach (var (major, states) in data.Majors) Console.WriteLine($"CMS {major} ({states.Cms}): {states.States.Count} states");
    Console.WriteLine($"Wrote {path}");
    return 0;
});

return await new RootCommand("Engage health check manifest generator (maintainer tool).") { generate, generateAll, verifyCommand, cmsStatesCommand }.Parse(args).InvokeAsync();

static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

// The alignment scripts folder: --align-scripts, else ENGAGE_ALIGN_SCRIPTS. Only the commands that run the scripts ask for it.
static string? ScriptsFolder(string? option)
{
    var folder = Trimmed(option) ?? Trimmed(Environment.GetEnvironmentVariable(AlignScripts.FolderVariable));
    if (folder is null)
    {
        Console.Error.WriteLine($"This command runs CompleteAlignSchema.sql. Download it from the Engage schema alignment guide on docs.umbraco.com, then give --align-scripts <folder> or set {AlignScripts.FolderVariable}.");
        return null;
    }
    if (Directory.Exists(folder)) return folder;
    Console.Error.WriteLine($"The alignment scripts folder does not exist: {folder}");
    return null;
}

static int UsageError(string message)
{
    Console.Error.WriteLine(message);
    return 3;
}

static (ReleaseCatalog Catalog, string Root)? LoadCatalog(string path)
{
    var file = Path.GetFullPath(path);
    try
    {
        return (ReleaseCatalog.Load(file), Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..")));
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"Cannot use the catalog {file}: {ex.Message}");
        return null;
    }
}

static async Task WriteAndPrint(Manifest manifest, string outFolder, CancellationToken ct)
{
    var path = await ManifestGenerator.WriteAsync(manifest, outFolder, ct);
    Console.WriteLine($"Wrote {path}: {manifest.Schema.Tables.Count} tables, last step {manifest.LastMigrationStep}, status {manifest.SchemaStatus ?? "(none)"}");
}

// The complete variant's step between the aligned installs and the snapshot: the major's published scripts, single-database mode.
static Func<string, CancellationToken, Task> CompleteAlignment(Release release, string scriptsFolder)
    => (cs, ct) => Verifier.RunAlignScriptsAsync(cs, release, scriptsFolder, Console.Out, ct);

// Runs CompleteAlignSchema.sql on the snapshotted aligned database and judges the "complete = fresh" assumption against the
// fresh manifest (a recorded known variant is reported as such), and whether the stored complete manifest reproduces.
// It never throws: a verify problem is a failed verdict, not a lost manifest.
static Func<string, CancellationToken, Task> VerifyHook(Release release, string scriptsFolder, string manifestsFolder, KnownVariantsFile known, List<(string Version, AssumptionVerdict Verdict)> verdicts)
    => async (cs, ct) =>
    {
        AssumptionVerdict verdict;
        try
        {
            var store = new ManifestStore(manifestsFolder, includeEmbedded: false);
            var fresh = store.Find(Products.Engage, release.Version, Variants.Fresh);
            verdict = fresh is null
                ? new AssumptionVerdict(new VerifyOutcome(false, $"no fresh manifest for {release.Version} in {manifestsFolder}", [], null, false), null, null)
                : await Verifier.CompleteAndJudgeAsync(cs, release, scriptsFolder, fresh, store.Find(Products.Engage, release.Version, Variants.Complete),
                    KnownVariants.Find(known, Products.Engage, release.Version, KnownVariants.CompleteEqualsFresh), Console.Out, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            verdict = new AssumptionVerdict(new VerifyOutcome(false, $"verify error: {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}", [], null, false), null, null);
        }
        verdict.WriteTo(Console.Out, $"verify {release.Version}");
        verdicts.Add((release.Version, verdict));
    };
