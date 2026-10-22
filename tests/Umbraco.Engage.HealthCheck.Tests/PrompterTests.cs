using Umbraco.Engage.HealthCheck.Core.Interactive;

namespace Umbraco.Engage.HealthCheck.Tests;

public class PrompterTests
{
    private const string Valid = "Server=localhost;Database=Engage;User Id=sa;Password=S3cret!pw";

    private static (Prompter Prompter, StringWriter Output) Make(string lineInput, params string[] masked)
    {
        var queue = new Queue<string>(masked);
        var output = new StringWriter();
        return (new Prompter(new StringReader(lineInput), output, () => queue.Count > 0 ? queue.Dequeue() : ""), output);
    }

    private static int Asked(StringWriter output) => output.ToString().Split("Q: ").Length - 1;

    [Fact]
    public void Masked_returns_valid_connection_string()
    {
        var (p, _) = Make("", Valid);
        Assert.Equal(Valid, p.AskConnectionString("Q: ", required: true));
    }

    [Fact]
    public void Required_empty_asks_again_then_gives_up_after_three_tries()
    {
        var (p, output) = Make("", "", "", "", Valid);
        Assert.Null(p.AskConnectionString("Q: ", required: true));
        Assert.Equal(3, Asked(output));
    }

    [Fact]
    public void Required_empty_then_valid_succeeds()
    {
        var (p, _) = Make("", "", Valid);
        Assert.Equal(Valid, p.AskConnectionString("Q: ", required: true));
    }

    [Fact]
    public void Invalid_string_says_so_and_asks_again()
    {
        var (p, output) = Make("", "not a connection string", Valid);
        Assert.Equal(Valid, p.AskConnectionString("Q: ", required: true));
        Assert.Contains("That is not a valid connection string.", output.ToString());
    }

    [Fact]
    public void Three_invalid_tries_give_up()
    {
        var (p, output) = Make("", "bad", "bad", "bad", Valid);
        Assert.Null(p.AskConnectionString("Q: ", required: true));
        Assert.Equal(3, Asked(output));
    }

    [Fact]
    public void Optional_empty_returns_null_without_retry()
    {
        var (p, output) = Make("", "", Valid);
        Assert.Null(p.AskConnectionString("Q: ", required: false));
        Assert.Equal(1, Asked(output));
    }

    [Fact]
    public void Typed_connection_string_is_never_echoed()
    {
        var (p, output) = Make("", "bad S3cret!pw", Valid);
        p.AskConnectionString("Q: ", required: true);
        Assert.DoesNotContain("S3cret", output.ToString());
    }

    [Fact]
    public void Ask_uses_default_on_empty_line()
    {
        var (p, _) = Make("\n");
        Assert.Equal("17.5.0", p.Ask("V [17.5.0]: ", "17.5.0"));
    }

    [Fact]
    public void Ask_returns_typed_value_trimmed()
    {
        var (p, _) = Make("  17.4.0 \n");
        Assert.Equal("17.4.0", p.Ask("V: ", "17.5.0"));
    }

    [Fact]
    public void Ask_without_default_and_empty_returns_null()
    {
        var (p, _) = Make("\n");
        Assert.Null(p.Ask("V: ", null));
    }

    [Fact]
    public void Version_prompt_offers_detect_automatically_as_the_default()
    {
        var (p, output) = Make("\n");
        Assert.Null(p.AskVersionOrDetect("Engage version to check against"));
        Assert.Equal("Engage version to check against [detect automatically]: ", output.ToString());
    }

    [Fact]
    public void Version_prompt_returns_a_typed_version()
    {
        var (p, _) = Make(" 17.4.0 \n");
        Assert.Equal("17.4.0", p.AskVersionOrDetect("V"));
    }

    [Fact]
    public void Version_prompt_accepts_detect_typed_out()
    {
        var (p, _) = Make("Detect\n");
        Assert.Null(p.AskVersionOrDetect("V"));
    }

    [Fact]
    public void Versions_are_sorted_highest_first_for_the_variant()
        => Assert.Equal(["17.10.0", "17.5.0", "9.0.0"], Prompter.VersionsFor(
            ["17.5.0.fresh", "9.0.0.fresh", "17.10.0.fresh", "17.5.0.upgraded", "17.12.0.upgraded"], "fresh"));

    [Fact]
    public void Versions_ignore_unparsable_entries_and_other_variants()
        => Assert.Equal(["1.0.0"], Prompter.VersionsFor(["1.0.0.fresh", "junk.fresh", "nodot"], "fresh"));

    [Fact]
    public void Versions_are_empty_when_variant_unknown()
        => Assert.Empty(Prompter.VersionsFor(["17.5.0.fresh"], "complete"));

    [Fact]
    public void Versions_summary_is_one_line_with_count_and_range()
    {
        var versions = Prompter.VersionsFor(["13.0.0.fresh", "17.5.0.fresh", "18.2.0.fresh", "18.2.0.aligned", "17.10.0.fresh"], "fresh");
        var line = Prompter.VersionsSummary(versions);
        Assert.Equal("Known Engage versions: 4 (13.0.0 to 18.2.0). Press Enter to detect automatically, or type a version.", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void Versions_summary_does_not_list_every_version()
        => Assert.DoesNotContain("17.5.0", Prompter.VersionsSummary(Prompter.VersionsFor(["13.0.0.fresh", "17.5.0.fresh", "18.2.0.fresh"], "fresh")));
}
