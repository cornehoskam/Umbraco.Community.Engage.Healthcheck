# Usage

The examples use `engage-healthcheck`. With the release zip that means `dotnet engage-healthcheck.dll`; from a clone of this repository, `dotnet run --project src/Umbraco.Community.Engage.Healthcheck.Cli --`.

## Requirements

- The .NET 10 runtime or newer for the release zip (`dotnet --list-runtimes` shows what you have). From source: the .NET 10 SDK. Docker is only needed to generate manifests and to run the tests, the fast ones included: they start a SQL Server container.
- On Linux the ICU library (`libicu`), which most distributions already have.
- Windows authentication works on Windows. On Linux and macOS it needs Kerberos to be set up (`kinit`); use a SQL login when it does not work.
- A login with `db_datareader` and `VIEW DEFINITION` on the Engage database.
- The same permissions on the Umbraco database, when Engage uses a separate database. Give that connection string with `--umbraco-db`. The upgrade record lives in the Umbraco database, and the tool never joins across the two databases.

## Connecting

Set the connection string in an environment variable so it stays out of your shell history.

```bash
export ENGAGE_HEALTHCHECK_ENGAGE_DB="Server=...;Database=...;..."
# Only when Engage uses a separate database:
export ENGAGE_HEALTHCHECK_UMBRACO_DB="Server=...;Database=...;..."
```

```powershell
$env:ENGAGE_HEALTHCHECK_ENGAGE_DB = "Server=...;Database=...;..."
$env:ENGAGE_HEALTHCHECK_UMBRACO_DB = "Server=...;Database=...;..."
```

```bash
engage-healthcheck check
```

Run `check` with no options and it prompts for what it needs. Connection strings are masked as you type. With no `--expect` it detects the version and checks against that.

### Server, database, login and password

Instead of a connection string you can give the parts separately. The tool builds the connection string for you. Use one way or the other: `--engage-db` cannot be combined with `--server`, `--database`, `--user`, `--password`, `--umbraco-database` or `--trust-server-certificate`. Mixing them stops the check with exit code 3.

- `--server` and `--database` are required.
- With `--user`, the tool uses a SQL login. Without it, it uses Windows (integrated) authentication.
- `--trust-server-certificate` is off by default. Turn it on for a server with a self-signed certificate, such as a local development server.
- `--umbraco-database` names a separate Umbraco database on the same server, with the same login. It only works together with `--server` and `--database`.

Give the password through `ENGAGE_HEALTHCHECK_PASSWORD` or the masked prompt, not through `--password`: a value on the command line ends up in your shell history. With `--user` and no password, an interactive run asks for it masked. A non-interactive run stops with exit code 3. The password is never printed, logged or written to the report. A password that starts with `-` must come from the environment variable or the prompt: the command-line parser would read it as an option. Spaces in a password are kept as typed.

```bash
export ENGAGE_HEALTHCHECK_SERVER="sqlserver.example.com"
export ENGAGE_HEALTHCHECK_DATABASE="EngageDb"
export ENGAGE_HEALTHCHECK_USER="engage_reader"
export ENGAGE_HEALTHCHECK_PASSWORD="..."   # or leave it out and answer the masked prompt
# Only when Engage uses a separate database on the same server:
export ENGAGE_HEALTHCHECK_UMBRACO_DATABASE="UmbracoDb"
# Only for a self-signed certificate:
export ENGAGE_HEALTHCHECK_TRUST_SERVER_CERTIFICATE=true

engage-healthcheck check
```

```powershell
$env:ENGAGE_HEALTHCHECK_SERVER = "sqlserver.example.com"
$env:ENGAGE_HEALTHCHECK_DATABASE = "EngageDb"
$env:ENGAGE_HEALTHCHECK_USER = "engage_reader"
$env:ENGAGE_HEALTHCHECK_PASSWORD = "..."   # or leave it out and answer the masked prompt
$env:ENGAGE_HEALTHCHECK_UMBRACO_DATABASE = "UmbracoDb"
$env:ENGAGE_HEALTHCHECK_TRUST_SERVER_CERTIFICATE = "true"

engage-healthcheck check
```

Windows authentication, with flags only (no secret involved):

```bash
engage-healthcheck check --server localhost --database EngageDb --trust-server-certificate
```

Interactive, `check` first asks "Connection string, or press Enter to enter server details". Press Enter and it asks for server, database, login (Enter means Windows authentication), password (masked, only when you gave a login), whether to trust the server certificate, and the Umbraco database (Enter means the same database).

| Option | What it does |
|---|---|
| `--expect <version>` | Check against this Engage version instead of the detected one, for example `--expect 17.5.0`. Use it to see what differs from a version you expect or plan to reach. |
| `--variant <variant>` | Manifest variant to check against: `fresh` (default), `aligned` or `complete`. With `--expect` and no `--variant`, the detected variant is used when the expected release has a manifest in that variant, and fresh otherwise. |
| `--forms <version>` | Engage Forms version to check against. Default: detected. |
| `--commerce <version>` | Engage Commerce version to check against. Default: detected. |
| `--umbraco-db <connection>` | Umbraco database, when it is separate from the Engage database. |
| `--engage-db <connection>` | Engage database. Prefer the environment variable. Cannot be combined with the separate options below. |
| `--server <server>` | SQL Server to connect to, instead of `--engage-db`. Needs `--database`. Env: `ENGAGE_HEALTHCHECK_SERVER`. |
| `--database <name>` | Engage database name, with `--server`. Env: `ENGAGE_HEALTHCHECK_DATABASE`. |
| `--user <login>` | SQL login. Without it, Windows authentication is used. Env: `ENGAGE_HEALTHCHECK_USER`. |
| `--password <password>` | Password for `--user`. Prefer `ENGAGE_HEALTHCHECK_PASSWORD` or the masked prompt, so it stays out of your shell history. |
| `--trust-server-certificate` | Trust the server's TLS certificate without checking it. Off by default. Env: `ENGAGE_HEALTHCHECK_TRUST_SERVER_CERTIFICATE=true`. |
| `--umbraco-database <name>` | Umbraco database on the same server with the same login, with `--server`. Env: `ENGAGE_HEALTHCHECK_UMBRACO_DATABASE`. Cannot be combined with `--umbraco-db`. |
| `--manifests <folder>` | Use manifest files from this folder instead of the built-in ones. Lets you check against a release newer than the tool. |
| `--report <path>` | Where to write the JSON report. Default: `engage-healthcheck-<dd-MM-yyyy-HHmm>.json` in the current folder. |
| `--no-color` | Turn colours off. See "Colours and the banner". |
| `--no-banner` | Do not print the start banner. |
| `--verbose` | Also show the recorded migration step (a GUID) on screen. The JSON report always has it. Env: `ENGAGE_HEALTHCHECK_VERBOSE=true`. |

Run `check --help` for the same list from the tool itself.

## Colours and the banner

A `check` run starts with a banner, and the result is coloured:

```text
 ___                           _  _          _ _   _       _           _
| __|_ _  __ _ __ _ __ _ ___  | || |___ __ _| | |_| |_  __| |_  ___ __| |__
| _|| ' \/ _` / _` / _` / -_) | __ / -_) _` | |  _| ' \/ _| ' \/ -_) _| / /
|___|_||_\__, \__,_\__, \___| |_||_\___\__,_|_|\__|_||_\__|_||_\___\__|_\_\
         |___/     |___/
v0.1.0
```

Colour is only an extra. Every label stays as text: `[Critical]`, `[Warning]`, `[Info]`, `HEALTHY`, `WARNINGS` and `CRITICAL`. Critical findings are red, warnings yellow, info dark gray, `Detected:` lines cyan, and the result is green, yellow or red. Errors on stderr are red.

| What turns it off | Colours | Banner |
|---|---|---|
| Output redirected to a file or a pipe (stdout for the banner and the report, stderr for errors) | off | off |
| `--no-color` or the `NO_COLOR` environment variable set to anything non-empty ([no-color.org](https://no-color.org)) | off | on |
| `TERM=dumb` | off | on |
| `--no-banner` | on | off |
| `CI` set to anything except empty, `false` or `0` (for example `CI=true`) | on | off |

The banner never shows for `--help` or for a usage error. The JSON report never holds colour codes.

## Reading the result

Example: a real run of `check --report hc.json` against a V17 development database with Engage, Forms and Commerce. Only the server and database names are replaced.

```text
Checking Engage database 'EngageDb' on server 'sqlserver'
Detected: Engage 17.4.0 (upgrade record and schema agree)
Recorded schema status:  Complete
Detected: Engage Forms between 17.1.0 and 17.1.2 (upgrade record and schema agree)
Detected: Engage Commerce 17.0.0 (upgrade record and schema agree)
Findings: 0 critical, 0 warning, 0 info

Summary
  Checked against:  Engage 17.4.0 (fresh)
  Engage database:  'EngageDb' on 'sqlserver'
  Umbraco database: same as Engage database
  Add-ons:          Engage Forms between 17.1.0 and 17.1.2 (checked against 17.1.2), Engage Commerce 17.0.0
  Findings:         0 critical, 0 warning, 0 info
  Result:           HEALTHY (exit code 0)
  Report:           hc.json
```

Engage 17.4.0 has the same database as 18.1.0. Here the Umbraco upgrade record says CMS 17, so the tool names 17.4.0 alone.

More detection lines, taken from the test output for the real manifests (`RealVariantDetectionTests`). A completed 17.2.x database, with CMS 17 evidence:

```text
Detected: Engage between 17.2.0 and 17.2.2 (alignment completed) (upgrade record allows up to 17.2.4)
```

An aligned 17.4.0 database without CMS evidence:

```text
Detected: Engage 17.4.0 or 18.1.0 (identical databases) (aligned: CompleteAlignSchema.sql has not been run yet)
```

Exit codes:

| Code | Meaning |
|---|---|
| `0` | Healthy. The database matches. |
| `1` | Warnings only. |
| `2` | At least one critical difference. |
| `3` | The check could not run, for example a bad connection string or missing permissions. |

The JSON report holds findings, versions and the recorded migration state. It holds no business data, and no server, database or user names, so it can be attached to a support ticket.

When the schema matches no known release, the tool names the closest one and says how many critical and warning differences remain. When the upgrade record and the schema disagree, it says so on its own line instead of trusting the record.

To check a download, compare it with `SHA256SUMS.txt` from the same release (`sha256sum -c SHA256SUMS.txt`).
