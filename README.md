# Umbraco Engage Health Check

Database health check for Umbraco Engage: schema, upgrade state and version detection. A read-only command-line check for Umbraco Engage databases. It finds out which Engage version your database is on, compares the database with the schema that version is supposed to have, and lists every difference. It never writes to your database.

> Experimental.

## What it does

- **Read-only.** It only reads schema information and the upgrade record. Nothing is changed.
- **Detects the installed Engage version**, and the Engage Forms and Engage Commerce add-ons, from the upgrade record and the real schema.
- **Shows a range** ("between 17.2.0 and 17.2.2") when several releases leave the database in exactly the same state. Those releases cannot be told apart from the database alone.
- **Shows the variant** of the database:
  - *fresh*: the shape a clean install creates.
  - *aligned*: the automatic schema alignment ran, but the manual `CompleteAlignSchema.sql` has not yet.
  - *alignment completed*: the manual script has been run too.
- **Analyses tables one by one** when the database is a mix of states. For each table that is not on the detected version, it says which releases of the same major and variant that table does match.
- **Uses Umbraco's own upgrade record** to tell identical v17 and v18 states apart. The CMS records its own upgrade state in the same database, and that tells the tool which Umbraco major sits under Engage.

## Supported versions

The lists below are kept in sync with `catalog/releases.json` by hand. A test (`ReadmeCatalogTests`) fails when they differ, so a new release cannot be added to the catalog without updating them.

- **Engage 13.0.0 to 18.2.0, 50 releases**
  - V13: 13.0.0, 13.1.0, 13.2.0, 13.3.0, 13.3.1, 13.4.0, 13.5.0, 13.5.1, 13.5.2, 13.5.3, 13.6.0, 13.6.1, 13.6.2, 13.7.0, 13.7.1, 13.7.2, 13.7.3, 13.7.4, 13.7.5, 13.8.0, 13.8.1
  - V16: 16.0.0, 16.0.1, 16.1.0, 16.1.1, 16.1.2, 16.2.0, 16.3.0, 16.3.1, 16.3.2, 16.3.3
  - V17: 17.0.0, 17.0.1, 17.0.2, 17.0.3, 17.0.4, 17.1.0, 17.1.1, 17.2.0, 17.2.1, 17.2.2, 17.2.3, 17.2.4, 17.3.0, 17.3.1, 17.4.0, 17.5.0
  - V18: 18.0.0, 18.1.0, 18.2.0
- **Engage Forms, 14 releases**
  - V13: 13.0.0, 13.1.0, 13.1.1, 13.2.0
  - V16: 16.0.0, 16.1.0, 16.2.0, 16.2.1, 16.2.2
  - V17: 17.0.0, 17.1.0, 17.1.1, 17.1.2
  - V18: 18.0.0
- **Engage Commerce, 6 releases**
  - V13: 13.0.0, 13.1.0
  - V16: 16.0.0, 16.0.1
  - V17: 17.0.0
  - V18: 18.0.0
- **Aligned and completed variants** for the 17 Engage releases that have an alignment step: 13.8.0 to 13.8.1, 16.3.0 to 16.3.3, 17.2.0 to 17.5.0 and 18.1.0 to 18.2.0.
- **104 manifests in total:** 70 fresh (Engage 50, Forms 14, Commerce 6), 17 aligned and 17 completed.

## Requirements

- .NET 10 SDK to run it from source. Running the check itself needs no Docker. Docker is needed to generate manifests and to run the tests, the fast ones included: they start a SQL Server container.
- A login with `db_datareader` and `VIEW DEFINITION` on the Engage database.
- The same permissions on the Umbraco database, when Engage uses a separate database. Give that connection string with `--umbraco-db`. The upgrade record lives in the Umbraco database, and the tool never joins across the two databases.

## Run it

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
dotnet run --project src/Umbraco.Engage.HealthCheck.Cli -- check
```

Run `check` with no options and it prompts for what it needs. Connection strings are masked as you type. With no `--expect` it detects the version and checks against that.

### Connect with server, database, login and password

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

dotnet run --project src/Umbraco.Engage.HealthCheck.Cli -- check
```

```powershell
$env:ENGAGE_HEALTHCHECK_SERVER = "sqlserver.example.com"
$env:ENGAGE_HEALTHCHECK_DATABASE = "EngageDb"
$env:ENGAGE_HEALTHCHECK_USER = "engage_reader"
$env:ENGAGE_HEALTHCHECK_PASSWORD = "..."   # or leave it out and answer the masked prompt
$env:ENGAGE_HEALTHCHECK_UMBRACO_DATABASE = "UmbracoDb"
$env:ENGAGE_HEALTHCHECK_TRUST_SERVER_CERTIFICATE = "true"

dotnet run --project src/Umbraco.Engage.HealthCheck.Cli -- check
```

Windows authentication, with flags only (no secret involved):

```bash
dotnet run --project src/Umbraco.Engage.HealthCheck.Cli -- check --server localhost --database EngageDb --trust-server-certificate
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
| `--no-color` | Turn colours off. See "Colours and the banner" below. |
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
v1.0.0
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

## Known variants and limits

- **Completed is not the same as fresh.** Running `CompleteAlignSchema.sql` leaves 29 `IX_<table>_key` indexes missing that a fresh install has, and some bit defaults stay `('False')` where a fresh install has `((0))`. That is why a separate *completed* variant exists. These differences are warnings only.
- **17.2.3 and 17.2.4 upgraded from 17.2.2 or older** keep `contentTypeId` nullable on `umbracoEngageAnalyticsUmbracoPageVariant`, while a fresh 17.2.3 or 17.2.4 creates it NOT NULL. Such a database is reported as between 17.2.0 and 17.2.2, with a note that the upgrade record allows up to 17.2.4.
- **Engage 18.0.0 on an aligned database.** Its first v18 migration step refuses to run until `CompleteAlignSchema.sql` has been run. The Engage schema stays on the v17 aligned shape while Umbraco itself is already on v18. The tool says the v18 upgrade is blocked. From 18.1.0 the check is a startup check, and Engage stays disabled until the script is run.
- **Limits of the 18.0.0 notice.** The "upgrade blocked" notice needs an exact match on an aligned v17 (or older) state and a recorded Umbraco upgrade state that belongs to CMS 18. A database that has drifted from the aligned shape (only a closest match), or whose Umbraco upgrade state is missing or unknown to the tool, gets no notice, even when the upgrade really is blocked.
- **Releases that cannot be told apart.** Some releases leave identical databases, for example 17.0.0 to 17.0.3, 17.2.0 to 17.2.2 and 17.3.0 to 17.3.1. Some states are also identical across majors, for example 17.4.0 and 18.1.0. Without Umbraco upgrade-record evidence the tool names both, as "17.4.0 or 18.1.0 (identical databases)", and checks against the older major. With that evidence, the CMS major picks one and the other is not shown.
- **Staged installs.** Engage 17.1.0 and Forms 16.2.0, 16.2.1, 17.1.0 and 17.1.1 cannot be fresh-installed: they fail on an empty database. Their manifests come from a staged install, the way customers reach them. Engage 17.1.0 is installed as 17.0.4 and upgraded. Forms is added to an existing Engage database.
- **Reporting tables** (`umbracoEngageReporting*`) are rebuilt by data generation and are not part of schema migrations. A stale upgrade can leave them different.
- **SQL Server only.** Engage does not run on SQLite.

The full list, with reasons and sources, is in `catalog/known-variants.json`.

## For maintainers

You only need this section to add a release or regenerate manifests. Running the health check needs none of it.

Everything the tool knows about a release is data: a catalog line plus generated manifests. Docker must be running for the generator.

The last-step check (run by `generate-all`), the aligned and complete variants, and `verify` need read access to the Engage, Forms and Commerce source repos: they read migration plans and SQL scripts at release tags. Those repos are not public, so these commands are for maintainers with that access. Only `generate` for a fresh manifest works from the public NuGet packages alone. Give the folder that holds those repos with `--repos <folder>` or set `ENGAGE_REPOS`. A command that needs it stops with a clear message when neither is set.

Generator commands (run from the repo root):

```bash
# One release. --variant: fresh (default), aligned, complete.
dotnet run --project src/Umbraco.Engage.HealthCheck.Generator -- generate --engage-version 17.5.0 --umbraco-version 17.5.3 --host hosts/Host.V17

# Every catalog release that has no manifest yet. Filters: --product, --major, --force, --variant.
dotnet run --project src/Umbraco.Engage.HealthCheck.Generator -- generate-all --variant fresh

# Check that "completed = fresh" (or, with --mode upgrade, "upgraded = fresh") holds for a release.
# Exit code 0 = holds, 4 = does not hold but is a recorded known variant, anything else = a real problem.
dotnet run --project src/Umbraco.Engage.HealthCheck.Generator -- verify --version 17.5.0

# Refresh which Umbraco major each CMS upgrade state belongs to (src/Umbraco.Engage.HealthCheck.Core/Data/cms-states.json).
dotnet run --project src/Umbraco.Engage.HealthCheck.Generator -- cms-states
```

`cms-states` reads the `umbraco.cms.infrastructure` packages from your NuGet cache (`--nuget` points elsewhere). Run it again after every Umbraco CMS release so detection can tell the majors apart.

### Add a new release

1. Add one line to `catalog/releases.json` (product, version, major, and `alignFrom` for Engage releases with an alignment step).
2. `generate-all --variant fresh`.
3. For an align-era Engage release: `generate-all --variant aligned`, then `generate-all --variant complete`.
4. `verify --version <release>`. Exit code 4 is a recorded known variant. Any other non-zero code needs a new entry in `catalog/known-variants.json` or a bug report.
5. Run `cms-states` again after each CMS release, and whenever the host CMS version in the catalog changes. It refuses to write the file when a host's own final CMS upgrade state maps to another major.
6. Update the snapshot tests that pin the shipped data on purpose:
   - `ReleaseCatalogTests.The_shipped_catalog_has_the_expected_release_counts`: the release counts per product.
   - `AlignedGeneratorTests.The_real_catalog_has_the_17_aligned_releases_and_none_for_16_2_0_or_18_0_0`: the aligned release count and list.
   - `RealManifestGroupsTests`: the expected state groups, when the new release joins or starts a group.
7. Update the release lists and manifest counts under "Supported versions" in this README. `ReadmeCatalogTests` fails until they match the catalog.
8. For a new Umbraco major: add a host project (`hosts/Host.V<major>`, copied from the newest one, with that major's CMS, Forms and Commerce packages), add it under `hosts` in `catalog/releases.json`, and run `cms-states`.
9. Run the tests.
10. Open a pull request. The manifest diff reads as what the release changed in the schema.

### Generator exit codes

These are the generator's codes, not the health check's (see "Reading the result" for those).

| Code | Meaning |
|---|---|
| `0` | Done. For `verify`: the assumption holds. |
| `1` | A release failed (`generate-all`), a verify assumption failed, or `cms-states` found a host whose final state maps to another major. |
| `2` | The catalog could not be read. |
| `3` | Usage error: a missing or conflicting option, a release that is not in the catalog, or no `--repos` and no `ENGAGE_REPOS` for a command that needs them. |
| `4` | `verify`, or `generate --verify`: the assumption does not hold, but the difference is a recorded known variant. |

### Tests

```bash
dotnet test --filter "Category!=E2E&Category!=E2EMatrix"   # fast, but starts a SQL Server container
dotnet test --filter "Category=E2E"                         # boots Umbraco and Engage in Docker: fresh install, add-ons and a mixed-state database
dotnet test --filter "Category=E2EMatrix"                   # every catalog release on a real database, a little over an hour (see "Verification")
```

`Category!=E2E` on its own also picks up the `E2EMatrix` tests, so the fast run excludes both. All of them need Docker: the fast tests use a SQL Server container (Testcontainers) for the schema reader and runner tests. The first run also pulls the SQL Server image.

## Verification

Every catalog release has been checked on a real database: [docs/verification/all-releases-21-10-2026.md](docs/verification/all-releases-21-10-2026.md).

Each case provisions a SQL Server database the way the catalog says (host, CMS version, `installFrom` staging, add-ons next to their `engage` release), runs the check with no `--expect` and the built-in manifests, and checks the detected version range, the variant, the upgrade record, the CMS major read from the database and the exit code. One aligned and one complete case per major are included. The run writes `docs/verification/all-releases-<dd-MM-yyyy>.md`.

```bash
# The complete cases run CompleteAlignSchema.sql from the release tag, so they need the Engage source repos.
export ENGAGE_REPOS="<folder with the Engage source repos>"
dotnet test --filter "Category=E2EMatrix"
```

It needs Docker and takes a little over an hour.

## License

MIT. See [LICENSE](LICENSE).
