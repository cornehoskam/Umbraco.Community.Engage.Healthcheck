# All releases verification, 21-10-2026

Produced by `AllReleasesMatrixTests` (tests/Umbraco.Community.Engage.Healthcheck.Tests/EndToEnd). For every release in `catalog/releases.json` it provisions a real SQL Server database the way the catalog says: the release's host and CMS version, `installFrom` staging, and add-ons installed next to their `engage` release (Engage first for `engage-first`). It then runs the real health check with no `--expect` and the embedded manifests, and checks the detection, the variant, the upgrade record, the CMS major read from the database and the exit code. The aligned and complete rows use the generator's aligned staging, and for complete the alignment scripts.

Command (the complete rows need `ENGAGE_ALIGN_SCRIPTS`, the folder with the alignment scripts from the Engage schema alignment guide):

```bash
dotnet test --filter "Category=E2EMatrix"
```

Result: 78 of 78 passed. Last run took 1:09:42 for 78 case(s).

| Product | Version | Host CMS | Staged? | Detected | cmsMajor | Exit code | Result | Duration |
|---|---|---|---|---|---|---|---|---|
| Engage | 13.0.0 | 13.16.2 (V13) | no | Detected: Engage between 13.0.0 and 13.2.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:36 |
| Engage | 13.1.0 | 13.16.2 (V13) | no | Detected: Engage between 13.0.0 and 13.2.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:34 |
| Engage | 13.2.0 | 13.16.2 (V13) | no | Detected: Engage between 13.0.0 and 13.2.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:37 |
| Engage | 13.3.0 | 13.16.2 (V13) | no | Detected: Engage between 13.3.0 and 13.3.1 (upgrade record and schema agree) | 13 | 0 | pass | 0:35 |
| Engage | 13.3.1 | 13.16.2 (V13) | no | Detected: Engage between 13.3.0 and 13.3.1 (upgrade record and schema agree) | 13 | 0 | pass | 0:35 |
| Engage | 13.4.0 | 13.16.2 (V13) | no | Detected: Engage 13.4.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:37 |
| Engage | 13.5.0 | 13.16.2 (V13) | no | Detected: Engage between 13.5.0 and 13.5.1 (upgrade record and schema agree) | 13 | 0 | pass | 0:35 |
| Engage | 13.5.1 | 13.16.2 (V13) | no | Detected: Engage between 13.5.0 and 13.5.1 (upgrade record and schema agree) | 13 | 0 | pass | 0:43 |
| Engage | 13.5.2 | 13.16.2 (V13) | no | Detected: Engage between 13.5.2 and 13.5.3 (upgrade record and schema agree) | 13 | 0 | pass | 0:40 |
| Engage | 13.5.3 | 13.16.2 (V13) | no | Detected: Engage between 13.5.2 and 13.5.3 (upgrade record and schema agree) | 13 | 0 | pass | 0:36 |
| Engage | 13.6.0 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:46 |
| Engage | 13.6.1 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:39 |
| Engage | 13.6.2 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:36 |
| Engage | 13.7.0 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:37 |
| Engage | 13.7.1 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:42 |
| Engage | 13.7.2 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:43 |
| Engage | 13.7.3 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:37 |
| Engage | 13.7.4 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:39 |
| Engage | 13.7.5 | 13.16.2 (V13) | no | Detected: Engage between 13.6.0 and 13.7.5 (upgrade record and schema agree) | 13 | 0 | pass | 0:35 |
| Engage | 13.8.0 | 13.16.2 (V13) | no | Detected: Engage between 13.8.0 and 13.8.1 (upgrade record and schema agree) | 13 | 0 | pass | 0:34 |
| Engage | 13.8.1 | 13.16.2 (V13) | no | Detected: Engage between 13.8.0 and 13.8.1 (upgrade record and schema agree) | 13 | 0 | pass | 0:34 |
| Engage | 16.0.0 | 16.5.1 (V16) | no | Detected: Engage between 16.0.0 and 16.2.0 (upgrade record and schema agree) | 16 | 0 | pass | 0:48 |
| Engage | 16.0.1 | 16.5.1 (V16) | no | Detected: Engage between 16.0.0 and 16.2.0 (upgrade record and schema agree) | 16 | 0 | pass | 0:58 |
| Engage | 16.1.0 | 16.5.1 (V16) | no | Detected: Engage between 16.0.0 and 16.2.0 (upgrade record and schema agree) | 16 | 0 | pass | 0:49 |
| Engage | 16.1.1 | 16.5.1 (V16) | no | Detected: Engage between 16.0.0 and 16.2.0 (upgrade record and schema agree) | 16 | 0 | pass | 1:01 |
| Engage | 16.1.2 | 16.5.1 (V16) | no | Detected: Engage between 16.0.0 and 16.2.0 (upgrade record and schema agree) | 16 | 0 | pass | 0:49 |
| Engage | 16.2.0 | 16.5.1 (V16) | no | Detected: Engage between 16.0.0 and 16.2.0 (upgrade record and schema agree) | 16 | 0 | pass | 0:50 |
| Engage | 16.3.0 | 16.5.1 (V16) | no | Detected: Engage between 16.3.0 and 16.3.2 (upgrade record and schema agree) (upgrade record allows up to 16.3.3) | 16 | 0 | pass | 0:52 |
| Engage | 16.3.1 | 16.5.1 (V16) | no | Detected: Engage between 16.3.0 and 16.3.2 (upgrade record and schema agree) (upgrade record allows up to 16.3.3) | 16 | 0 | pass | 0:49 |
| Engage | 16.3.2 | 16.5.1 (V16) | no | Detected: Engage between 16.3.0 and 16.3.2 (upgrade record and schema agree) (upgrade record allows up to 16.3.3) | 16 | 0 | pass | 0:46 |
| Engage | 16.3.3 | 16.5.1 (V16) | no | Detected: Engage 16.3.3 (upgrade record and schema agree) | 16 | 0 | pass | 0:44 |
| Engage | 17.0.0 | 17.5.3 (V17) | no | Detected: Engage between 17.0.0 and 17.0.3 (upgrade record and schema agree) | 17 | 0 | pass | 0:49 |
| Engage | 17.0.1 | 17.5.3 (V17) | no | Detected: Engage between 17.0.0 and 17.0.3 (upgrade record and schema agree) | 17 | 0 | pass | 0:54 |
| Engage | 17.0.2 | 17.5.3 (V17) | no | Detected: Engage between 17.0.0 and 17.0.3 (upgrade record and schema agree) | 17 | 0 | pass | 0:46 |
| Engage | 17.0.3 | 17.5.3 (V17) | no | Detected: Engage between 17.0.0 and 17.0.3 (upgrade record and schema agree) | 17 | 0 | pass | 0:59 |
| Engage | 17.0.4 | 17.5.3 (V17) | no | Detected: Engage 17.0.4 (upgrade record and schema agree) | 17 | 0 | pass | 0:45 |
| Engage | 17.1.0 | 17.5.3 (V17) | from 17.0.4 | Detected: Engage between 17.1.0 and 17.1.1 (upgrade record and schema agree) | 17 | 0 | pass | 1:27 |
| Engage | 17.1.1 | 17.5.3 (V17) | no | Detected: Engage between 17.1.0 and 17.1.1 (upgrade record and schema agree) | 17 | 0 | pass | 0:48 |
| Engage | 17.2.0 | 17.5.3 (V17) | no | Detected: Engage between 17.2.0 and 17.2.2 (upgrade record and schema agree) (upgrade record allows up to 17.2.4) | 17 | 0 | pass | 0:55 |
| Engage | 17.2.1 | 17.5.3 (V17) | no | Detected: Engage between 17.2.0 and 17.2.2 (upgrade record and schema agree) (upgrade record allows up to 17.2.4) | 17 | 0 | pass | 0:43 |
| Engage | 17.2.2 | 17.5.3 (V17) | no | Detected: Engage between 17.2.0 and 17.2.2 (upgrade record and schema agree) (upgrade record allows up to 17.2.4) | 17 | 0 | pass | 0:55 |
| Engage | 17.2.3 | 17.5.3 (V17) | no | Detected: Engage between 17.2.3 and 17.2.4 (upgrade record and schema agree) | 17 | 0 | pass | 0:50 |
| Engage | 17.2.4 | 17.5.3 (V17) | no | Detected: Engage between 17.2.3 and 17.2.4 (upgrade record and schema agree) | 17 | 0 | pass | 0:46 |
| Engage | 17.3.0 | 17.5.3 (V17) | no | Detected: Engage between 17.3.0 and 17.3.1 (upgrade record and schema agree) | 17 | 0 | pass | 0:50 |
| Engage | 17.3.1 | 17.5.3 (V17) | no | Detected: Engage between 17.3.0 and 17.3.1 (upgrade record and schema agree) | 17 | 0 | pass | 0:48 |
| Engage | 17.4.0 | 17.5.3 (V17) | no | Detected: Engage 17.4.0 (upgrade record and schema agree) | 17 | 0 | pass | 0:49 |
| Engage | 17.5.0 | 17.5.3 (V17) | no | Detected: Engage 17.5.0 (upgrade record and schema agree) | 17 | 0 | pass | 0:43 |
| Engage | 18.0.0 | 18.2.1 (V18) | no | Detected: Engage 18.0.0 (upgrade record and schema agree) | 18 | 0 | pass | 0:51 |
| Engage | 18.1.0 | 18.2.1 (V18) | no | Detected: Engage 18.1.0 (upgrade record and schema agree) | 18 | 0 | pass | 0:48 |
| Engage | 18.2.0 | 18.2.1 (V18) | no | Detected: Engage 18.2.0 (upgrade record and schema agree) | 18 | 0 | pass | 0:45 |
| Engage Forms | 13.0.0 | 13.16.2 (V13) | no | Detected: Engage Forms between 13.0.0 and 13.2.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:39 |
| Engage Forms | 13.1.0 | 13.16.2 (V13) | no | Detected: Engage Forms between 13.0.0 and 13.2.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:48 |
| Engage Forms | 13.1.1 | 13.16.2 (V13) | no | Detected: Engage Forms between 13.0.0 and 13.2.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:37 |
| Engage Forms | 13.2.0 | 13.16.2 (V13) | no | Detected: Engage Forms between 13.0.0 and 13.2.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:37 |
| Engage Forms | 16.0.0 | 16.5.1 (V16) | no | Detected: Engage Forms between 16.0.0 and 16.1.0 (upgrade record and schema agree) | 16 | 0 | pass | 0:54 |
| Engage Forms | 16.1.0 | 16.5.1 (V16) | no | Detected: Engage Forms between 16.0.0 and 16.1.0 (upgrade record and schema agree) | 16 | 0 | pass | 0:51 |
| Engage Forms | 16.2.0 | 16.5.1 (V16) | Engage first | Detected: Engage Forms between 16.2.0 and 16.2.2 (upgrade record and schema agree) | 16 | 0 | pass | 1:42 |
| Engage Forms | 16.2.1 | 16.5.1 (V16) | Engage first | Detected: Engage Forms between 16.2.0 and 16.2.2 (upgrade record and schema agree) | 16 | 0 | pass | 1:32 |
| Engage Forms | 16.2.2 | 16.5.1 (V16) | no | Detected: Engage Forms between 16.2.0 and 16.2.2 (upgrade record and schema agree) | 16 | 0 | pass | 0:50 |
| Engage Forms | 17.0.0 | 17.5.3 (V17) | no | Detected: Engage Forms 17.0.0 (upgrade record and schema agree) | 17 | 0 | pass | 0:48 |
| Engage Forms | 17.1.0 | 17.5.3 (V17) | Engage first | Detected: Engage Forms between 17.1.0 and 17.1.2 (upgrade record and schema agree) | 17 | 0 | pass | 1:34 |
| Engage Forms | 17.1.1 | 17.5.3 (V17) | Engage first | Detected: Engage Forms between 17.1.0 and 17.1.2 (upgrade record and schema agree) | 17 | 0 | pass | 1:28 |
| Engage Forms | 17.1.2 | 17.5.3 (V17) | no | Detected: Engage Forms between 17.1.0 and 17.1.2 (upgrade record and schema agree) | 17 | 0 | pass | 0:49 |
| Engage Forms | 18.0.0 | 18.2.1 (V18) | no | Detected: Engage Forms 18.0.0 (upgrade record and schema agree) | 18 | 0 | pass | 0:52 |
| Engage Commerce | 13.0.0 | 13.16.2 (V13) | no | Detected: Engage Commerce between 13.0.0 and 13.1.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:38 |
| Engage Commerce | 13.1.0 | 13.16.2 (V13) | no | Detected: Engage Commerce between 13.0.0 and 13.1.0 (upgrade record and schema agree) | 13 | 0 | pass | 0:39 |
| Engage Commerce | 16.0.0 | 16.5.1 (V16) | no | Detected: Engage Commerce between 16.0.0 and 16.0.1 (upgrade record and schema agree) | 16 | 0 | pass | 0:51 |
| Engage Commerce | 16.0.1 | 16.5.1 (V16) | no | Detected: Engage Commerce between 16.0.0 and 16.0.1 (upgrade record and schema agree) | 16 | 0 | pass | 1:10 |
| Engage Commerce | 17.0.0 | 17.5.3 (V17) | no | Detected: Engage Commerce 17.0.0 (upgrade record and schema agree) | 17 | 0 | pass | 1:04 |
| Engage Commerce | 18.0.0 | 18.2.1 (V18) | no | Detected: Engage Commerce 18.0.0 (upgrade record and schema agree) | 18 | 0 | pass | 1:07 |
| Engage | 13.8.1 (aligned) | 13.16.2 (V13) | from 13.7.5 | Detected: Engage between 13.8.0 and 13.8.1 (aligned: CompleteAlignSchema.sql has not been run yet) | 13 | 0 | pass | 1:15 |
| Engage | 13.8.1 (complete) | 13.16.2 (V13) | from 13.7.5 | Detected: Engage between 13.8.0 and 13.8.1 (alignment completed) | 13 | 0 | pass | 1:14 |
| Engage | 16.3.3 (aligned) | 16.5.1 (V16) | from 16.1.2 | Detected: Engage between 16.3.0 and 16.3.3 (aligned: CompleteAlignSchema.sql has not been run yet) | 16 | 0 | pass | 1:35 |
| Engage | 16.3.3 (complete) | 16.5.1 (V16) | from 16.1.2 | Detected: Engage 16.3.3 (alignment completed) | 16 | 0 | pass | 1:38 |
| Engage | 17.5.0 (aligned) | 17.5.3 (V17) | from 17.1.1 | Detected: Engage 17.5.0 (aligned: CompleteAlignSchema.sql has not been run yet) | 17 | 0 | pass | 1:31 |
| Engage | 17.5.0 (complete) | 17.5.3 (V17) | from 17.1.1 | Detected: Engage 17.5.0 (alignment completed) | 17 | 0 | pass | 1:29 |
| Engage | 18.2.0 (aligned) | 18.2.1 (V18) | from 17.1.1 | Detected: Engage 18.2.0 (aligned: CompleteAlignSchema.sql has not been run yet) | 18 | 0 | pass | 1:37 |
| Engage | 18.2.0 (complete) | 18.2.1 (V18) | from 17.1.1 | Detected: Engage 18.2.0 (alignment completed) | 18 | 0 | pass | 1:27 |
