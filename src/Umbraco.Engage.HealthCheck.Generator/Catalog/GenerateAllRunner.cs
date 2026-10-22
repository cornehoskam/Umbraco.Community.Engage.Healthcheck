using System.Text;
using Umbraco.Engage.HealthCheck.Core.Manifests;

namespace Umbraco.Engage.HealthCheck.Generator.Catalog;

public enum ReleaseStatus { Done, Skipped, Failed, KnownFailure }

public sealed record ReleaseResult(Release Release, ReleaseStatus Status, string? Reason = null, CheckOutcome? Check = null)
{
    public bool IsMismatch => Check is { Status: CheckStatus.Mismatch };
}

/// <summary>Walks the catalog one release at a time: skip what exists, generate the rest, keep going on failure.</summary>
public static class GenerateAllRunner
{
    public delegate Task<IReadOnlyList<Manifest>> Generate(IReadOnlyList<HostSpec> stages, string? installedFrom, CancellationToken ct);

    public static async Task<IReadOnlyList<ReleaseResult>> RunAsync(
        ReleaseCatalog catalog, IEnumerable<Release> releases, string outFolder, bool force, string catalogRoot,
        Generate generate, Func<Manifest, Release, CheckOutcome> check, TextWriter log, CancellationToken ct, string variant = Variants.Fresh)
    {
        var results = new List<ReleaseResult>();
        // The aligned and complete variants both start from the alignFrom release.
        var aligned = Variants.IsAligned(variant) || Variants.IsComplete(variant);
        // These variants cover only releases with an alignFrom; a known failure is about the fresh install only.
        foreach (var release in aligned ? releases.Where(r => r.AlignFrom is not null).Select(r => r with { KnownFailure = null }) : releases)
        {
            ct.ThrowIfCancellationRequested();
            var existing = ExistingPath(release, outFolder, aligned ? variant.ToLowerInvariant() : Variants.Fresh);
            if (!force && File.Exists(existing))
            {
                log.WriteLine($"[skip] {release}: manifest exists");
                try
                {
                    var manifest = ManifestSerializer.Deserialize(File.ReadAllText(existing));
                    results.Add(Checked(new ReleaseResult(release, ReleaseStatus.Skipped), manifest, check));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    results.Add(new ReleaseResult(release, ReleaseStatus.Failed, $"existing manifest {existing} is unreadable: {Flatten(ex.Message)}"));
                }
                continue;
            }
            if (!force && release.KnownFailure is not null)
            {
                log.WriteLine($"[known] {release}: {release.KnownFailure}");
                results.Add(new ReleaseResult(release, ReleaseStatus.KnownFailure, release.KnownFailure));
                continue;
            }

            log.WriteLine($"[run ] {release} on {release.Major}...");
            try
            {
                var stages = aligned ? catalog.AlignedStagesFor(release, catalogRoot) : catalog.StagesFor(release, catalogRoot);
                if (stages.FirstOrDefault(s => !Directory.Exists(s.HostProject)) is { } missing)
                {
                    results.Add(Failure(release, $"host missing: {missing.HostProject}"));
                    continue;
                }

                var manifests = await generate(stages, aligned ? release.AlignFrom : release.InstallFrom, ct);
                var own = manifests.Single(m => m.Product == release.Product && m.Version == release.Version);
                var path = await ManifestGenerator.WriteAsync(own, outFolder, ct);
                log.WriteLine($"Wrote {path}: {own.Schema.Tables.Count} tables, last step {own.LastMigrationStep}");
                results.Add(Checked(new ReleaseResult(release, ReleaseStatus.Done), own, check));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var reason = Flatten(ex.Message);
                results.Add(Failure(release, reason.Length > 300 ? reason[..300] + "..." : reason));
            }
        }
        return results;
    }

    // A release the catalog marks as a known failure stays one even when forced and failing again.
    private static ReleaseResult Failure(Release release, string reason)
        => release.KnownFailure is not null
            ? new ReleaseResult(release, ReleaseStatus.KnownFailure, $"{release.KnownFailure} (this run: {reason})")
            : new ReleaseResult(release, ReleaseStatus.Failed, reason);

    private static string Flatten(string text) => text.ReplaceLineEndings(" ");

    private static string ExistingPath(Release release, string outFolder, string variant)
        => Path.GetFullPath(Path.Combine(outFolder, ManifestStore.RelativePath(release.Product, release.Version, variant)));

    private static ReleaseResult Checked(ReleaseResult result, Manifest manifest, Func<Manifest, Release, CheckOutcome> check)
    {
        try { return result with { Check = check(manifest, result.Release) }; }
        catch (Exception ex) { return result with { Check = new CheckOutcome(CheckStatus.Unverified, Flatten(ex.Message)) }; }
    }

    /// <summary>A failure is a failed release or a last-step mismatch. Unverified results and known failures are not failures.</summary>
    public static bool HasFailures(IEnumerable<ReleaseResult> results) => results.Any(r => r.Status == ReleaseStatus.Failed || r.IsMismatch);

    public static string Summary(IReadOnlyList<ReleaseResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"{"Release",-26}{"Result",-14}{"Last step",-11}Note");
        foreach (var r in results)
        {
            var check = r.Check is null ? "-" : r.Check.Status switch
            {
                CheckStatus.Match => "match",
                CheckStatus.Mismatch => "MISMATCH",
                _ => "unverified",
            };
            var result = r.Status == ReleaseStatus.KnownFailure ? "known failure" : r.Status.ToString().ToLowerInvariant();
            var note = r.Status is ReleaseStatus.Failed or ReleaseStatus.KnownFailure ? r.Reason : r.Check is { Status: not CheckStatus.Match } ? r.Check.Detail : "";
            sb.AppendLine($"{r.Release,-26}{result,-14}{check,-11}{note}");
        }
        int Count(ReleaseStatus s) => results.Count(r => r.Status == s);
        var unverified = results.Count(r => r.Check is { Status: CheckStatus.Unverified });
        sb.AppendLine();
        sb.AppendLine($"done {Count(ReleaseStatus.Done)}, skipped {Count(ReleaseStatus.Skipped)}, failed {Count(ReleaseStatus.Failed)}, known failures {Count(ReleaseStatus.KnownFailure)}, " +
                      $"unverified {unverified}, last-step mismatches {results.Count(r => r.IsMismatch)}");
        var checkedResults = results.Where(r => r.Check is not null).ToList();
        if (checkedResults.Count > 0 && checkedResults.All(r => r.Check!.Status == CheckStatus.Unverified))
            sb.AppendLine("WARNING: every result is unverified, so nothing was checked against the release tags. Check --repos.");
        return sb.ToString();
    }
}
