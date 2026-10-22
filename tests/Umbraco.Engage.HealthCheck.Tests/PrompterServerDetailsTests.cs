using Umbraco.Engage.HealthCheck.Core.Interactive;

namespace Umbraco.Engage.HealthCheck.Tests;

public class PrompterServerDetailsTests
{
    private const string Secret = "S3cret!pw";

    private static (Prompter Prompter, StringWriter Output) Make(string lines, params string[] masked)
    {
        var queue = new Queue<string>(masked);
        var output = new StringWriter();
        return (new Prompter(new StringReader(lines), output, () => queue.Count > 0 ? queue.Dequeue() : ""), output);
    }

    [Fact]
    public void A_connection_string_is_taken_as_it_is()
    {
        var (p, output) = Make("", "Server=a;Database=b");
        var answer = p.AskEngageConnection()!;
        Assert.Equal("Server=a;Database=b", answer.ConnectionString);
        Assert.Null(answer.Details);
        Assert.Equal(Prompter.ConnectionStringOrServerDetailsQuestion, output.ToString());
    }

    [Fact]
    public void Enter_asks_for_the_details_in_order_with_a_login()
    {
        var (p, output) = Make("db1\nEngage\nsvc\ny\nUmbracoDb\n", "", Secret);
        var details = p.AskEngageConnection()!.Details!;
        Assert.Equal(new ServerDetails("db1", "Engage", "svc", Secret, true, "UmbracoDb"), details);
        var text = output.ToString();
        var positions = new[]
            {
                "Connection string, or press Enter to enter server details: ",
                "Server: ",
                "Database: ",
                "Login (press Enter for Windows authentication): ",
                "Password: ",
                "Trust server certificate (y/N): ",
                "Umbraco database (press Enter if it is the same database): ",
            }
            .Select(q => text.IndexOf(q, StringComparison.Ordinal)).ToList();
        Assert.All(positions, i => Assert.True(i >= 0));
        Assert.Equal(positions.OrderBy(i => i), positions);
    }

    [Fact]
    public void Enter_as_login_means_windows_authentication_and_skips_the_password()
    {
        var (p, output) = Make("db1\nEngage\n\n\n\n", "");
        var details = p.AskEngageConnection()!.Details!;
        Assert.Equal(new ServerDetails("db1", "Engage"), details);
        Assert.False(details.HasUser);
        Assert.DoesNotContain("Password", output.ToString());
    }

    [Fact]
    public void Trust_defaults_to_no()
    {
        var (p, _) = Make("db1\nEngage\n\n\n\n", "");
        Assert.False(p.AskEngageConnection()!.Details!.TrustServerCertificate);
    }

    [Fact]
    public void Server_is_asked_again_when_empty_and_given_up_after_three_tries()
    {
        var (p, output) = Make("\n\n\n", "");
        Assert.Equal("No server given.", p.AskEngageConnection()!.Error);
        Assert.Equal(3, output.ToString().Split("Server: ").Length - 1);
    }

    [Fact]
    public void Invalid_string_says_so_and_asks_again()
    {
        var (p, output) = Make("", "bad", "Server=a;Database=b");
        Assert.Equal("Server=a;Database=b", p.AskEngageConnection()!.ConnectionString);
        Assert.Contains("That is not a valid connection string.", output.ToString());
    }

    [Fact]
    public void The_password_is_never_echoed()
    {
        var (p, output) = Make("db1\nEngage\nsvc\n\n\n", "", Secret);
        p.AskEngageConnection();
        Assert.DoesNotContain("S3cret", output.ToString());
    }

    [Fact]
    public void A_typed_password_keeps_its_spaces()
    {
        var (p, _) = Make("db1\nEngage\nsvc\n\n\n", "", " pw ");
        Assert.Equal(" pw ", p.AskEngageConnection()!.Details!.Password);
    }

    [Fact]
    public void AskPassword_keeps_spaces_and_a_connection_string_is_still_trimmed()
    {
        var (p, _) = Make("", " pw ");
        Assert.Equal(" pw ", p.AskPassword("P: "));
        var (q, _) = Make("", "  Server=a;Database=b  ");
        Assert.Equal("Server=a;Database=b", q.AskEngageConnection()!.ConnectionString);
    }

    [Fact]
    public void An_empty_password_gives_a_clear_error_after_three_tries()
    {
        var (p, output) = Make("db1\nEngage\nsvc\n", "", "", "", "");
        var answer = p.AskEngageConnection()!;
        Assert.Equal("No password given.", answer.Error);
        Assert.Equal(3, output.ToString().Split("Password: ").Length - 1);
    }

    [Fact]
    public void Answers_never_print_their_secrets()
    {
        var answer = new ConnectionAnswer("Server=a;Password=S3cret!pw", new ServerDetails("s", "d", "u", Secret));
        Assert.DoesNotContain("S3cret", answer.ToString());
        Assert.DoesNotContain("S3cret", new ConnectionBuildResult("Server=a;Password=S3cret!pw", null, null).ToString());
    }

    [Fact]
    public void Umbraco_db_given_up_front_is_not_dropped_when_server_details_are_typed()
    {
        var (p, _) = Make("db1\nEngage\n\n\n\n", "");
        var details = p.AskEngageConnection()!.Details!;
        var error = ConnectionResolver.CheckExclusive(null, "Server=a;Database=um", details);
        Assert.NotNull(error);
        Assert.Contains("--umbraco-db", error);
    }

    [Fact]
    public void Umbraco_db_and_a_typed_umbraco_database_say_not_both()
    {
        var (p, _) = Make("db1\nEngage\n\n\nUm\n", "");
        var details = p.AskEngageConnection()!.Details!;
        Assert.Equal("Use either --umbraco-db or --umbraco-database, not both.", ConnectionResolver.CheckExclusive(null, "Server=a;Database=um", details));
    }
}
