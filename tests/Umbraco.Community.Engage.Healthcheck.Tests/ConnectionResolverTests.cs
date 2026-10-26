using Microsoft.Data.SqlClient;
using Umbraco.Community.Engage.Healthcheck.Core.Interactive;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

public class ConnectionResolverTests
{
    private const string Secret = "S3cret!pw";
    private static readonly ServerDetails None = new();

    [Fact]
    public void Sql_login_builds_a_string_with_user_and_password()
    {
        var result = ConnectionResolver.Build(new("db1,1433", "Engage", "svc_engage", Secret));
        var builder = new SqlConnectionStringBuilder(result.Engage);
        Assert.True(result.Ok);
        Assert.Equal("db1,1433", builder.DataSource);
        Assert.Equal("Engage", builder.InitialCatalog);
        Assert.Equal("svc_engage", builder.UserID);
        Assert.Equal(Secret, builder.Password);
        Assert.False(builder.IntegratedSecurity);
        Assert.False(builder.TrustServerCertificate);
        Assert.Null(result.Umbraco);
    }

    [Fact]
    public void A_value_with_a_semicolon_stays_one_value()
    {
        // Nothing is concatenated: the password cannot smuggle in a second keyword.
        var builder = new SqlConnectionStringBuilder(ConnectionResolver.Build(new("db1", "Engage", "sa", "x;Database=Other")).Engage);
        Assert.Equal("Engage", builder.InitialCatalog);
        Assert.Equal("x;Database=Other", builder.Password);
    }

    [Fact]
    public void Without_a_login_it_uses_integrated_authentication()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionResolver.Build(new("localhost", "Engage")).Engage);
        Assert.True(builder.IntegratedSecurity);
        Assert.Equal("", builder.UserID);
        Assert.Equal("", builder.Password);
    }

    [Fact]
    public void Trust_flag_is_off_by_default_and_on_when_asked()
    {
        Assert.False(new SqlConnectionStringBuilder(ConnectionResolver.Build(new("s", "d")).Engage).TrustServerCertificate);
        Assert.True(new SqlConnectionStringBuilder(ConnectionResolver.Build(new("s", "d", TrustServerCertificate: true)).Engage).TrustServerCertificate);
    }

    [Fact]
    public void Umbraco_database_uses_the_same_server_login_and_trust()
    {
        var result = ConnectionResolver.Build(new("db1", "Engage", "sa", Secret, true, "Umbraco"));
        var engage = new SqlConnectionStringBuilder(result.Engage);
        var umbraco = new SqlConnectionStringBuilder(result.Umbraco);
        Assert.Equal("Umbraco", umbraco.InitialCatalog);
        Assert.Equal(engage.DataSource, umbraco.DataSource);
        Assert.Equal(engage.UserID, umbraco.UserID);
        Assert.Equal(engage.Password, umbraco.Password);
        Assert.True(umbraco.TrustServerCertificate);
    }

    [Fact]
    public void Describer_works_on_the_built_string_without_leaking_the_login()
    {
        var text = ConnectionDescriber.DescribeWithServerWord(ConnectionResolver.Build(new("db1", "Engage", "svc_engage", Secret)).Engage!);
        Assert.Equal("'Engage' on server 'db1'", text);
    }

    [Theory]
    [InlineData(null, "Engage")]
    [InlineData("db1", null)]
    [InlineData("  ", "Engage")]
    [InlineData("db1", "")]
    public void Server_and_database_are_both_required(string? server, string? database)
    {
        var result = ConnectionResolver.Build(new(server, database));
        Assert.False(result.Ok);
        Assert.Contains("--server and --database", result.Error);
    }

    [Fact]
    public void Login_without_a_password_is_an_error()
    {
        var result = ConnectionResolver.Build(new("db1", "Engage", "sa"));
        Assert.False(result.Ok);
        Assert.Contains("needs a password", result.Error);
    }

    [Fact]
    public void Password_without_a_login_is_an_error_that_does_not_show_it()
    {
        var result = ConnectionResolver.Build(new("db1", "Engage", null, Secret));
        Assert.False(result.Ok);
        Assert.DoesNotContain("S3cret", result.Error);
    }

    [Fact]
    public void A_value_the_builder_rejects_gives_the_standard_message_without_the_value()
    {
        var result = ConnectionResolver.Build(new ServerDetails("db1", "Engage", "sa", Secret) with { Server = "db1\0" });
        Assert.Equal("That is not a valid connection string.", result.Error);
    }

    [Fact]
    public void Error_messages_never_contain_the_password()
    {
        var messages = new[]
        {
            ConnectionResolver.CheckExclusive("Server=a;Database=b", null, new("s", "d", "u", Secret)),
            ConnectionResolver.CheckExclusive(null, "Server=a;Database=b", new("s", "d", "u", Secret, false, "x")),
            ConnectionResolver.Build(new(null, null, "u", Secret)).Error,
            ConnectionResolver.Build(new("s", "d", null, Secret)).Error,
        };
        Assert.All(messages, m =>
        {
            Assert.NotNull(m);
            Assert.DoesNotContain("S3cret", m);
        });
    }

    [Fact]
    public void ToString_hides_the_password()
        => Assert.DoesNotContain("S3cret", new ServerDetails("s", "d", "u", Secret).ToString());

    [Fact]
    public void Nothing_given_is_fine()
        => Assert.Null(ConnectionResolver.CheckExclusive(null, null, None));

    [Fact]
    public void Engage_db_alone_and_separate_options_alone_are_fine()
    {
        Assert.Null(ConnectionResolver.CheckExclusive("Server=a;Database=b", "Server=a;Database=c", None));
        Assert.Null(ConnectionResolver.CheckExclusive(null, null, new("s", "d", "u", "p", true, "um")));
    }

    [Theory]
    [InlineData("server")]
    [InlineData("database")]
    [InlineData("user")]
    [InlineData("password")]
    [InlineData("umbraco-database")]
    [InlineData("trust")]
    public void Engage_db_cannot_be_combined_with_any_separate_option(string which)
    {
        var details = which switch
        {
            "server" => new ServerDetails(Server: "s"),
            "database" => new ServerDetails(Database: "d"),
            "user" => new ServerDetails(User: "u"),
            "password" => new ServerDetails(Password: "p"),
            "umbraco-database" => new ServerDetails(UmbracoDatabase: "um"),
            _ => new ServerDetails(TrustServerCertificate: true),
        };
        var error = ConnectionResolver.CheckExclusive("Server=a;Database=b", null, details);
        Assert.NotNull(error);
        Assert.Contains("--engage-db", error);
    }

    [Fact]
    public void Umbraco_db_cannot_be_combined_with_umbraco_database()
    {
        var error = ConnectionResolver.CheckExclusive(null, "Server=a;Database=c", new("s", "d", UmbracoDatabase: "um"));
        Assert.Contains("--umbraco-db or --umbraco-database", error);
    }

    [Fact]
    public void Umbraco_db_cannot_follow_separate_engage_values()
        => Assert.NotNull(ConnectionResolver.CheckExclusive(null, "Server=a;Database=c", new("s", "d")));

    [Fact]
    public void Umbraco_database_needs_server_and_database()
        => Assert.Contains("--umbraco-database", ConnectionResolver.CheckExclusive(null, null, new(UmbracoDatabase: "um")));

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("y", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Env_style_booleans(string? text, bool expected)
        => Assert.Equal(expected, ConnectionResolver.IsTrue(text));

    [Fact]
    public void A_password_with_spaces_is_kept_as_it_is()
        => Assert.Equal(" pw ", new SqlConnectionStringBuilder(ConnectionResolver.Build(new("s", "d", "u", " pw ")).Engage).Password);

    [Fact]
    public void Blank_values_count_as_not_given()
    {
        Assert.False(ConnectionResolver.AnySeparateOption(new("  ", " ", " ", "", false, " ")));
        Assert.Null(ConnectionResolver.CheckExclusive("Server=a;Database=b", null, new("  ", " ")));
    }

    [Fact]
    public void A_value_from_the_environment_is_named_in_the_message()
    {
        var sources = new ConnectionSources(Server: "ENGAGE_HEALTHCHECK_SERVER");
        var error = ConnectionResolver.CheckExclusive("Server=a;Database=b", null, new("s", "d"), sources);
        Assert.Contains("ENGAGE_HEALTHCHECK_SERVER", error);
        Assert.Contains("--database", error);
    }
}
