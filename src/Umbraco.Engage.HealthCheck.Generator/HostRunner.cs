using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Umbraco.Engage.HealthCheck.Generator;

/// <summary>
/// One host build: the Engage and CMS versions, plus the optional add-ons. An add-on version needs the matching
/// Umbraco Forms / Umbraco Commerce version too.
/// </summary>
public sealed record HostSpec(
    string HostProject,
    string EngageVersion,
    string UmbracoVersion,
    string? FormsVersion = null,
    string? UmbracoFormsVersion = null,
    string? CommerceVersion = null,
    string? UmbracoCommerceVersion = null);

/// <summary>Builds a host project for one Engage version and starts it as a child process.</summary>
public static class HostRunner
{
    public static async Task<string> BuildAsync(HostSpec spec, CancellationToken ct)
    {
        if (spec.FormsVersion is not null && spec.UmbracoFormsVersion is null)
            throw new ArgumentException("An Engage Forms version needs an Umbraco Forms version.", nameof(spec));
        if (spec.CommerceVersion is not null && spec.UmbracoCommerceVersion is null)
            throw new ArgumentException("An Engage Commerce version needs an Umbraco Commerce version.", nameof(spec));

        // Every version is in the folder name, so different builds never share an output.
        var name = $"{Path.GetFileName(spec.HostProject)}-{spec.EngageVersion}-{spec.UmbracoVersion}";
        string[] args = ["build", spec.HostProject, "-c", "Release", $"-p:EngageVersion={spec.EngageVersion}", $"-p:UmbracoVersion={spec.UmbracoVersion}"];
        if (spec.FormsVersion is not null)
        {
            name += $"-forms-{spec.FormsVersion}-{spec.UmbracoFormsVersion}";
            args = [.. args, $"-p:EngageFormsVersion={spec.FormsVersion}", $"-p:UmbracoFormsVersion={spec.UmbracoFormsVersion}"];
        }
        if (spec.CommerceVersion is not null)
        {
            name += $"-commerce-{spec.CommerceVersion}-{spec.UmbracoCommerceVersion}";
            args = [.. args, $"-p:EngageCommerceVersion={spec.CommerceVersion}", $"-p:UmbracoCommerceVersion={spec.UmbracoCommerceVersion}"];
        }

        var output = Path.Combine(Path.GetTempPath(), "engage-healthcheck-hosts", name);
        var exit = await RunToEndAsync("dotnet", [.. args, "-o", output], ct);
        if (exit != 0) throw new InvalidOperationException($"Building {spec.HostProject} ({name}) failed (exit {exit}).");
        return Path.Combine(output, Path.GetFileName(spec.HostProject) + ".dll");
    }

    public static Process Start(string hostDll, string connectionString, string adminPassword, string logFile)
    {
        var psi = new ProcessStartInfo("dotnet", [hostDll])
        {
            WorkingDirectory = Path.GetDirectoryName(hostDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{FreePort()}";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["ConnectionStrings__umbracoDbDSN"] = connectionString;
        psi.Environment["ConnectionStrings__umbracoDbDSN_ProviderName"] = "Microsoft.Data.SqlClient";
        psi.Environment["Umbraco__CMS__Unattended__UnattendedUserPassword"] = adminPassword;

        var process = Process.Start(psi)!;
        var gate = new object();
        void Append(string? line)
        {
            if (line is null) return;
            try { lock (gate) File.AppendAllText(logFile, line + Environment.NewLine); }
            catch (IOException) { /* A log write error must not crash the generator while the host keeps running. */ }
        }
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public static async Task StopAsync(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<int> RunToEndAsync(string file, string[] args, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })!;
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
