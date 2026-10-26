using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Community.Engage.Healthcheck.Core.Reporting;

public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // The report is a local file, never served as HTML: keep "record+schema" and the like readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultFileName(DateTimeOffset localNow) => $"engage-healthcheck-{localNow:dd-MM-yyyy-HHmm}.json";

    public static async Task WriteAsync(Report report, string path)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, Options);
    }
}
