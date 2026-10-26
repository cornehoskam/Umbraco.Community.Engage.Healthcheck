using Umbraco.Community.Engage.Healthcheck.Core.Comparison;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

public class SeverityRulesTests
{
    [Fact]
    public void Every_finding_kind_has_a_rule()
    {
        foreach (var kind in Enum.GetValues<FindingKind>())
            SeverityRules.For(kind);
    }
}
