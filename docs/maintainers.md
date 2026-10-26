# Maintainers

This page is for adding releases, regenerating manifests and running the tests. Running a health check needs none of it.

Everything the tool knows about a release is data: a catalog line plus generated manifests. Docker must be running for the generator.

Everything the generator uses is public:

- **The Engage, Forms and Commerce packages from nuget.org.** The hosts install them, and `generate-all` checks each manifest's last upgrade step against the migration plan in that release's package. It reads the packages from your NuGet cache (`--nuget` points elsewhere), which the host builds fill.
- **The alignment scripts.** The complete variant, `generate --variant aligned --verify` and `verify` run `EnsureDataConsistency.sql` and `CompleteAlignSchema.sql`. Download them from the [Engage schema alignment guide](https://docs.umbraco.com/umbraco-engage/upgrading/schema-alignment-guide) for each major you need. Put them in one folder, or in `V13`, `V16`, `V17` and `V18` subfolders, and give it with `--align-scripts <folder>` or set `ENGAGE_ALIGN_SCRIPTS`. A command that needs the scripts stops with a clear message when neither is set.

Generator commands (run from the repo root):

```bash
# One release. --variant: fresh (default), aligned, complete.
dotnet run --project src/Umbraco.Community.Engage.Healthcheck.Generator -- generate --engage-version 17.5.0 --umbraco-version 17.5.3 --host hosts/Host.V17

# Every catalog release that has no manifest yet. Filters: --product, --major, --force, --variant.
dotnet run --project src/Umbraco.Community.Engage.Healthcheck.Generator -- generate-all --variant fresh

# Check that "completed = fresh" (or, with --mode upgrade, "upgraded = fresh") holds for a release.
# Exit code 0 = holds, 4 = does not hold but is a recorded known variant, anything else = a real problem.
dotnet run --project src/Umbraco.Community.Engage.Healthcheck.Generator -- verify --version 17.5.0

# Refresh which Umbraco major each CMS upgrade state belongs to (src/Umbraco.Community.Engage.Healthcheck.Core/Data/cms-states.json).
dotnet run --project src/Umbraco.Community.Engage.Healthcheck.Generator -- cms-states
```

`cms-states` reads the `umbraco.cms.infrastructure` packages from your NuGet cache (`--nuget` points elsewhere). Run it again after every Umbraco CMS release so detection can tell the majors apart.

## Add a new release

1. Add one line to `catalog/releases.json` (product, version, major, and `alignFrom` for Engage releases with an alignment step).
2. `generate-all --variant fresh`.
3. For an align-era Engage release: `generate-all --variant aligned`, then `generate-all --variant complete`.
4. `verify --version <release>`. Exit code 4 is a recorded known variant. Any other non-zero code needs a new entry in `catalog/known-variants.json` or a bug report.
5. Run `cms-states` again after each CMS release, and whenever the host CMS version in the catalog changes. It refuses to write the file when a host's own final CMS upgrade state maps to another major.
6. Update the snapshot tests that pin the shipped data on purpose:
   - `ReleaseCatalogTests.The_shipped_catalog_has_the_expected_release_counts`: the release counts per product.
   - `AlignedGeneratorTests.The_real_catalog_has_the_17_aligned_releases_and_none_for_16_2_0_or_18_0_0`: the aligned release count and list.
   - `RealManifestGroupsTests`: the expected state groups, when the new release joins or starts a group.
7. Update the release lists and manifest counts in `docs/supported-versions.md`. `ReadmeCatalogTests` fails until they match the catalog.
8. For a new Umbraco major: add a host project (`hosts/Host.V<major>`, copied from the newest one, with that major's CMS, Forms and Commerce packages), add it under `hosts` in `catalog/releases.json`, and run `cms-states`.
9. Run the tests.
10. Open a pull request. The manifest diff reads as what the release changed in the schema.

## Generator exit codes

These are the generator's codes, not the health check's (see "Reading the result" in [usage.md](usage.md) for those).

| Code | Meaning |
|---|---|
| `0` | Done. For `verify`: the assumption holds. |
| `1` | A release failed (`generate-all`), a verify assumption failed, or `cms-states` found a host whose final state maps to another major. |
| `2` | The catalog could not be read. |
| `3` | Usage error: a missing or conflicting option, a release that is not in the catalog, or no `--align-scripts` and no `ENGAGE_ALIGN_SCRIPTS` for a command that needs them. |
| `4` | `verify`, or `generate --verify`: the assumption does not hold, but the difference is a recorded known variant. |

## Tests

```bash
dotnet test --filter "Category!=E2E&Category!=E2EMatrix"   # fast, but starts a SQL Server container
dotnet test --filter "Category=E2E"                         # boots Umbraco and Engage in Docker: fresh install, add-ons and a mixed-state database
dotnet test --filter "Category=E2EMatrix"                   # every catalog release on a real database, a little over an hour (see "Verification")
```

`Category!=E2E` on its own also picks up the `E2EMatrix` tests, so the fast run excludes both. All of them need Docker: the fast tests use a SQL Server container (Testcontainers) for the schema reader and runner tests. The first run also pulls the SQL Server image.

## Verification

Every catalog release has been checked on a real database: [verification/all-releases-21-10-2026.md](verification/all-releases-21-10-2026.md).

Each case provisions a SQL Server database the way the catalog says (host, CMS version, `installFrom` staging, add-ons next to their `engage` release), runs the check with no `--expect` and the built-in manifests, and checks the detected version range, the variant, the upgrade record, the CMS major read from the database and the exit code. One aligned and one complete case per major are included. The run writes `docs/verification/all-releases-<dd-MM-yyyy>.md`.

```bash
# The complete cases run the alignment scripts from the Engage schema alignment guide (see above).
export ENGAGE_ALIGN_SCRIPTS="<folder with the downloaded scripts>"
dotnet test --filter "Category=E2EMatrix"
```

It needs Docker and takes a little over an hour.
