using Microsoft.Data.SqlClient;

namespace Umbraco.Community.Engage.Healthcheck.Core;

public sealed class MissingPermissionException(string message) : Exception(message);

/// <summary>
/// Checks the login's rights before the schema is read. Without VIEW DEFINITION, SQL Server still lists the tables but
/// hides definitions such as default constraint text, so the schema read would fail on empty data instead of saying why.
/// </summary>
public static class PermissionCheck
{
    public const string MissingViewDefinition =
        "The login can read the Engage database but is missing VIEW DEFINITION, so it cannot see the schema. "
        + "Grant it on that database with: GRANT VIEW DEFINITION TO [<login>];";

    /// <summary>
    /// Throws <see cref="MissingPermissionException"/> when the login lacks VIEW DEFINITION on the dbo schema, where the
    /// Engage tables live. A database-level grant and db_owner both cover it.
    /// </summary>
    public static async Task EnsureViewDefinitionAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'VIEW DEFINITION')", conn);
        if (await cmd.ExecuteScalarAsync(ct) is not 1) throw new MissingPermissionException(MissingViewDefinition);
    }
}
