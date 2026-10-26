using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

/// <summary>The CMS upgrade state tells identical Engage states of two majors apart (synthetic manifests and state lists).</summary>
public class CmsEvidenceTests
{
    private static readonly DatabaseSchema Schema = SchemaBuilder.Baseline();

    private static Manifest M(string version, string variant = Variants.Fresh, string? status = "Complete")
        => new(1, Products.Engage, version, variant, "x", MigrationState.StateKey, "S", status, Schema);

    // 17.4.0 and 18.1.0 record the same step, status and schema, as the real manifests do.
    private static readonly IReadOnlyList<StateGroup> Twins = VersionIndex.Build([M("17.4.0"), M("18.1.0")]);

    private static readonly CmsStateData States = new("test", new Dictionary<string, CmsMajorStates>
    {
        ["17"] = new("17.5.3", ["{aaaaaaaa-0000-0000-0000-000000000017}", "{bbbbbbbb-0000-0000-0000-000000000000}"]),
        ["18"] = new("18.2.1", ["{bbbbbbbb-0000-0000-0000-000000000000}", "{cccccccc-0000-0000-0000-000000000018}"]),
    });

    [Theory]
    [InlineData("{aaaaaaaa-0000-0000-0000-000000000017}", 17)]
    [InlineData("{CCCCCCCC-0000-0000-0000-000000000018}", 18)]
    [InlineData("{bbbbbbbb-0000-0000-0000-000000000000}", 17)] // in both lists: the lowest major
    [InlineData("{dddddddd-0000-0000-0000-000000000000}", null)] // unknown: no evidence
    [InlineData(null, null)]
    public void The_CMS_major_is_the_lowest_major_that_knows_the_state(string? state, int? major)
        => Assert.Equal(major, CmsStates.MajorOf(state, States));

    [Theory]
    [InlineData(17, "17.4.0")]
    [InlineData(18, "18.1.0")]
    public void CMS_evidence_breaks_the_17_vs_18_tie_and_drops_the_other_major(int cms, string expected)
    {
        var d = Detector.Detect(Products.Engage, Schema, "S", Twins, "Complete", cms).ToResult();
        Assert.Equal(expected, d.Versions);
        Assert.Empty(d.Alternatives);
        Assert.Equal(cms, d.CmsMajor);
    }

    [Fact]
    public void Without_evidence_the_older_major_is_named_and_the_twin_is_identical()
    {
        var d = Detector.Detect(Products.Engage, Schema, "S", Twins, "Complete").ToResult();
        Assert.Equal("17.4.0", d.Versions);
        Assert.Equal([new DetectionAlternative("18.1.0", Variants.Fresh, Identical: true)], d.Alternatives);
        Assert.Null(d.CmsMajor);
        Assert.Equal("Detected: Engage 17.4.0 or 18.1.0 (identical databases) (upgrade record and schema agree)", ConsoleReporter.Describe("Engage", d, "S"));
    }

    [Fact]
    public void An_unknown_CMS_state_is_the_same_as_no_evidence()
    {
        var cms = CmsStates.MajorOf("{dddddddd-0000-0000-0000-000000000000}", States);
        Assert.Equal("17.4.0", Detector.Detect(Products.Engage, Schema, "S", Twins, "Complete", cms).ToResult().Versions);
        Assert.True(Detector.Detect(Products.Engage, Schema, "S", Twins, "Complete", cms).ToResult().Alternatives.Single().Identical);
    }

    [Fact]
    public void The_record_never_reaches_into_another_major()
    {
        var d = Detector.Detect(Products.Engage, Schema, "S", Twins, "Complete").ToResult();
        Assert.Equal("17.4.0", d.RecordedUpTo);
        Assert.DoesNotContain("allows up to", ConsoleReporter.Describe("Engage", d, "S"));
    }

    private static string Render(DetectionResult d, Manifest target)
    {
        var sw = new StringWriter();
        ConsoleReporter.Write(Report.Create(target, new(true, "S", target.SchemaStatus), [], [], DateTimeOffset.UnixEpoch, d, Report.FromDetection), sw, new ConsoleContext("db", null, null));
        return sw.ToString().ReplaceLineEndings("\n");
    }

    private static readonly IReadOnlyList<StateGroup> AlignedTwins = VersionIndex.Build([M("17.4.0", Variants.Aligned, "Aligned"), M("18.1.0", Variants.Aligned, "Aligned")]);

    [Fact]
    public void Console_for_identical_aligned_twins_without_evidence_names_both_and_is_conditional()
    {
        var d = Detector.Detect(Products.Engage, Schema, "S", AlignedTwins, "Aligned").ToResult();
        var text = Render(d, M("17.4.0", Variants.Aligned, "Aligned"));
        Assert.Contains("Detected: Engage 17.4.0 or 18.1.0 (identical databases) (aligned: CompleteAlignSchema.sql has not been run yet)\n" +
                        "  On Engage 18.1.0 or later: Engage is disabled until CompleteAlignSchema.sql is run\n", text);
        Assert.DoesNotContain("Also matches", text);
    }

    [Theory]
    [InlineData(17, "Detected: Engage 17.4.0 (aligned: CompleteAlignSchema.sql has not been run yet)\n", false)]
    [InlineData(18, "Detected: Engage 18.1.0 (aligned: CompleteAlignSchema.sql has not been run yet)\n  Engage is disabled until CompleteAlignSchema.sql is run\n", true)]
    public void Console_for_aligned_twins_with_CMS_evidence_is_definite(int cms, string expected, bool disabled)
    {
        var d = Detector.Detect(Products.Engage, Schema, "S", AlignedTwins, "Aligned", cms).ToResult();
        var text = Render(d, M(d.Versions!, Variants.Aligned, "Aligned"));
        Assert.Contains(expected, text);
        Assert.Equal(disabled, text.Contains("disabled", StringComparison.Ordinal));
        Assert.DoesNotContain("Also matches", text);
    }
}

public class EmbeddedCmsStatesTests
{
    [Theory]
    [InlineData("{6A7D3B80-8B64-4E41-A7C0-02EC39336E97}", 16)] // final 16.4 state, also the CMS 17 and 18 plans' starting point
    [InlineData("{3A1A8047-74AE-491A-B2C4-0BAE4A1289EC}", 17)] // CMS 17.6.0 AddContentTypeIdIndexForContent, also in 18.2.1
    [InlineData("{6FE4656E-8B8D-452F-AE2A-438A615B61BC}", 18)] // only in CMS 18
    public void Real_states_map_to_the_lowest_major(string state, int major)
        => Assert.Equal(major, CmsStates.MajorOf(state));

    [Fact]
    public void Every_host_major_has_states()
        => Assert.All(new[] { "13", "16", "17", "18" }, m => Assert.NotEmpty(CmsStates.Embedded.Majors[m].States));
}
