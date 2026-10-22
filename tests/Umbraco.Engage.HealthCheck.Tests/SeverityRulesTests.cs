using Umbraco.Engage.HealthCheck.Core.Comparison;

namespace Umbraco.Engage.HealthCheck.Tests;

public class SeverityRulesTests
{
    [Fact]
    public void Every_finding_kind_has_a_rule()
    {
        foreach (var kind in Enum.GetValues<FindingKind>())
            SeverityRules.For(kind);
    }
}
