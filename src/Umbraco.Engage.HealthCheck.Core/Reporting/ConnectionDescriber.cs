using Microsoft.Data.SqlClient;

namespace Umbraco.Engage.HealthCheck.Core.Reporting;

/// <summary>Names a database for the screen. Only server and database; never user id, password or any other keyword.</summary>
public static class ConnectionDescriber
{
    public static string Describe(string connectionString)
    {
        var (database, server) = Parts(connectionString);
        return $"'{database}' on '{server}'";
    }

    public static string DescribeWithServerWord(string connectionString)
    {
        var (database, server) = Parts(connectionString);
        return $"'{database}' on server '{server}'";
    }

    private static (string Database, string Server) Parts(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return (OrDefault(builder.InitialCatalog), OrDefault(builder.DataSource));
    }

    private static string OrDefault(string value) => string.IsNullOrWhiteSpace(value) ? "(default)" : value;
}
