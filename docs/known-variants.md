# Known variants and limits

Most databases match one release exactly. These are the cases where the result needs some explaining. The full list, with the manifests that show each case, is in `catalog/known-variants.json`.

## Completed is not the same as fresh

Running `CompleteAlignSchema.sql` leaves 29 `IX_<table>_key` indexes missing that a fresh install has, and some bit defaults stay `('False')` where a fresh install has `((0))`. That is why a separate *completed* variant exists. These differences are warnings only.

## 17.2.3 and 17.2.4 upgraded from 17.2.2 or older

A 17.2.3 or 17.2.4 database upgraded from 17.2.2 or older keeps `contentTypeId` nullable on `umbracoEngageAnalyticsUmbracoPageVariant`, while a fresh 17.2.3 or 17.2.4 creates it NOT NULL. Such a database is reported as between 17.2.0 and 17.2.2, with a note that the upgrade record allows up to 17.2.4.

## 16.3.0 to 16.3.2 and 17.2.0 to 17.2.2 completed with today's script

The `CompleteAlignSchema.sql` in the Engage schema alignment guide also makes `contentTypeId` on `umbracoEngageAnalyticsUmbracoPageVariant` NOT NULL, which the scripts shipped with those releases did not. Such a database is reported as 16.3.3, or as between 17.2.3 and 17.2.4, alignment completed and healthy. Those releases record the same upgrade step, so the database alone cannot tell them apart.

## Engage 18.0.0 on an aligned database

The first migration step of Engage 18.0.0 refuses to run until `CompleteAlignSchema.sql` has been run. The Engage schema stays on the v17 aligned shape while Umbraco itself is already on v18. The tool says the v18 upgrade is blocked. From 18.1.0 the check is a startup check, and Engage stays disabled until the script is run.

## Limits of the 18.0.0 notice

The "upgrade blocked" notice needs an exact match on an aligned v17 (or older) state and a recorded Umbraco upgrade state that belongs to CMS 18. A database that has drifted from the aligned shape (only a closest match), or whose Umbraco upgrade state is missing or unknown to the tool, gets no notice, even when the upgrade really is blocked.

## Releases that cannot be told apart

Some releases leave identical databases, for example 17.0.0 to 17.0.3, 17.2.0 to 17.2.2 and 17.3.0 to 17.3.1. Some states are also identical across majors, for example 17.4.0 and 18.1.0. Without Umbraco upgrade-record evidence the tool names both, as "17.4.0 or 18.1.0 (identical databases)", and checks against the older major. With that evidence, the CMS major picks one and the other is not shown.

## Staged installs

Engage 17.1.0 and Forms 16.2.0, 16.2.1, 17.1.0 and 17.1.1 cannot be fresh-installed: they fail on an empty database. Their manifests come from a staged install, the way customers reach them. Engage 17.1.0 is installed as 17.0.4 and upgraded. Forms is added to an existing Engage database.

## Reporting tables

The `umbracoEngageReporting*` tables are rebuilt by data generation and are not part of schema migrations. A stale upgrade can leave them different.

## SQL Server only

Engage does not run on SQLite.

