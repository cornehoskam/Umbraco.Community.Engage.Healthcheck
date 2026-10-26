# Engage Healthcheck

[![Latest release](https://img.shields.io/github/v/release/cornehoskam/Umbraco.Community.Engage.Healthcheck?include_prereleases&color=0273B3)](https://github.com/cornehoskam/Umbraco.Community.Engage.Healthcheck/releases)
[![CI](https://github.com/cornehoskam/Umbraco.Community.Engage.Healthcheck/actions/workflows/ci.yml/badge.svg)](https://github.com/cornehoskam/Umbraco.Community.Engage.Healthcheck/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/cornehoskam/Umbraco.Community.Engage.Healthcheck?color=8AB803)](LICENSE)

A small command-line tool for developers who need to know what state an [Umbraco Engage](https://umbraco.com/products/umbraco-engage/) database is in. Point it at a database and it tells you which Engage version the schema belongs to, whether the schema alignment has been completed, and every table, column, index or key that differs from what that version should have.

It only reads. Nothing in your database is changed.

> [!WARNING]
> This is a personal community project and it is experimental. It is not made, endorsed or supported by Umbraco, so please don't contact Umbraco support about it; open an issue here instead. The results can be wrong or incomplete. Take a backup and check a finding yourself before you act on it.

## Install

You need the .NET 10 runtime or newer (`dotnet --list-runtimes` shows what you have).

1. Download `engage-healthcheck-<version>.zip` from the [latest release](https://github.com/cornehoskam/Umbraco.Community.Engage.Healthcheck/releases/latest). The same zip works on Windows, Linux and macOS.
2. Unpack it somewhere.
3. Run `dotnet engage-healthcheck.dll check` from that folder.

## Run a check

Run `check` without options and it asks for what it needs: a connection string, or the server, database and login one by one. Passwords and connection strings are masked while you type.

```bash
dotnet engage-healthcheck.dll check
```

You can also pass everything up front. This uses Windows authentication against a local server with a self-signed certificate:

```bash
dotnet engage-healthcheck.dll check --server localhost --database MyEngageDb --trust-server-certificate
```

For a SQL login, put the password in `ENGAGE_HEALTHCHECK_PASSWORD` rather than on the command line, so it stays out of your shell history. The login needs `db_datareader` and `VIEW DEFINITION` on the database. If Engage has its own database apart from Umbraco, add `--umbraco-database` (same server) or `--umbraco-db` (a full connection string), because Umbraco's upgrade record lives there.

A run looks like this:

```text
Checking Engage database 'MyEngageDb' on server 'localhost'
Detected: Engage 17.5.0 (aligned: CompleteAlignSchema.sql has not been run yet)
Recorded schema status:  Aligned
Add-ons: none detected
Findings: 0 critical, 0 warning, 0 info

Summary
  Checked against:  Engage 17.5.0 (aligned)
  Engage database:  'MyEngageDb' on 'localhost'
  Umbraco database: same as Engage database
  Add-ons:          none
  Findings:         0 critical, 0 warning, 0 info
  Result:           HEALTHY (exit code 0)
  Report:           engage-healthcheck-22-10-2026-1407.json
```

The exit code is 0 when the database matches, 1 when there are only warnings, 2 when there is at least one critical difference, and 3 when the check could not run at all. Next to the console output you get a JSON report with every finding. It contains no data from your tables and no server, database or user names, so it is safe to attach to an issue.

Use `--expect 17.5.0` to compare against a version you expect or are about to upgrade to, instead of the detected one. Every option, environment variable and the colour settings are in [docs/usage.md](docs/usage.md), and `check --help` lists them too.

## What it can tell you

Detection uses two sources: the upgrade step Engage records in `umbracoKeyValue`, and the actual schema. When they disagree, the tool says so instead of trusting the record. That is usually the first clue in a broken upgrade.

Some releases leave exactly the same database behind, for example 17.0.0 to 17.0.3. Then you get a range like "between 17.0.0 and 17.0.3", because nothing in the database can tell them apart. A few v17 and v18 releases are identical too (17.4.0 and 18.1.0); Umbraco's own upgrade record decides which major it is.

Since Engage 13.8, 16.3 and 17.2 there is a schema alignment step, and a database can be in one of three shapes: fresh (a clean install), aligned (the automatic part ran, `CompleteAlignSchema.sql` has not) or alignment completed. The tool tells you which one you have. If only some tables are behind, it lists those tables and the versions they do match.

It knows about Engage 13.0.0 up to 18.2.0, Engage Forms and Engage Commerce, 70 releases in total. The full list is in [docs/supported-versions.md](docs/supported-versions.md). Cases where the result needs some explaining, such as databases completed with today's alignment script, are in [docs/known-variants.md](docs/known-variants.md).

## Building from source

Clone the repository and run `dotnet run --project src/Umbraco.Community.Engage.Healthcheck.Cli -- check` with the .NET 10 SDK. The tests need Docker, because they start a SQL Server container.

Adding a new Engage release is mostly data: a line in `catalog/releases.json` and a generated schema snapshot. [docs/maintainers.md](docs/maintainers.md) explains the generator, the tests and how every release was verified on a real database.

## License

MIT, see [LICENSE](LICENSE). The packages it uses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
