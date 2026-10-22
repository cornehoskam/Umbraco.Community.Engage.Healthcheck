using Umbraco.Engage.HealthCheck.Core.Reporting;

namespace Umbraco.Engage.HealthCheck.Tests;

public class ConnectionDescriberTests
{
    [Fact]
    public void Shows_database_and_server_only()
    {
        var text = ConnectionDescriber.Describe("Server=localhost;Database=EngageDb;User Id=sa;Password=S3cret!pw;TrustServerCertificate=true");
        Assert.Equal("'EngageDb' on 'localhost'", text);
    }

    [Fact]
    public void Never_contains_user_or_password()
    {
        var text = ConnectionDescriber.Describe("Data Source=db1;Initial Catalog=Engage;User ID=svc_engage;Password=S3cret!pw");
        Assert.DoesNotContain("svc_engage", text);
        Assert.DoesNotContain("S3cret", text);
        Assert.DoesNotContain("User", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_parts_show_default()
        => Assert.Equal("'(default)' on '(default)'", ConnectionDescriber.Describe("Integrated Security=true"));

    [Fact]
    public void Verbose_form_has_the_word_server()
        => Assert.Equal("'Engage' on server 'db1'", ConnectionDescriber.DescribeWithServerWord("Server=db1;Database=Engage;Password=x"));
}
