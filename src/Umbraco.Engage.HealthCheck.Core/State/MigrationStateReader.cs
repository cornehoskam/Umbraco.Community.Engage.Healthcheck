using Microsoft.Data.SqlClient;

namespace Umbraco.Engage.HealthCheck.Core.State;

public sealed record KeyValues(bool TableFound, IReadOnlyDictionary<string, string?> Values)
{
    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
}

public static class MigrationStateReader
{
    public static async Task<MigrationState> ReadAsync(string connectionString, CancellationToken ct = default)
    {
        var kv = await ReadKeysAsync(connectionString, [MigrationState.StateKey, MigrationState.SchemaStatusKey], ct);
        return kv.TableFound
            ? new MigrationState(true, kv.Get(MigrationState.StateKey), kv.Get(MigrationState.SchemaStatusKey))
            : new MigrationState(false, null, null);
    }

    public static async Task<KeyValues> ReadKeysAsync(string connectionString, IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using (var exists = new SqlCommand("SELECT OBJECT_ID(N'dbo.umbracoKeyValue', N'U')", conn))
            if (await exists.ExecuteScalarAsync(ct) is DBNull or null)
                return new KeyValues(false, values);
        if (keys.Count == 0) return new KeyValues(true, values);

        await using var cmd = new SqlCommand(string.Empty, conn);
        var names = new List<string>();
        var i = 0;
        foreach (var key in keys)
        {
            var name = "@k" + i++;
            names.Add(name);
            cmd.Parameters.AddWithValue(name, key);
        }
        cmd.CommandText = $"SELECT [key], [value] FROM umbracoKeyValue WHERE [key] IN ({string.Join(", ", names)})";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return new KeyValues(true, values);
    }
}
