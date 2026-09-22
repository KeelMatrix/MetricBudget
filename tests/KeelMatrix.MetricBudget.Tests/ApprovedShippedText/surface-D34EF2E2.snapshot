# Privacy and telemetry

Metric tags are one of the most privacy-sensitive parts of an application: they routinely carry customer
identifiers, tenant names, URLs, resource names, and tokens. This page states exactly what leaves a session.

## What a report contains

- meter names, meter versions, instrument names, and instrument kinds;
- configured limits and observed counts;
- tag **keys**;
- safety-bound state and structured problem records.

## What never leaves a session

- tag **values** - never printed, logged, serialized, or sent anywhere;
- metric values - the session does not read them;
- samples of the observed workload.

Reports and assertion messages exclude tag and metric values, but they retain application-supplied meter names, meter
versions, instrument names, and tag keys. Those identifiers can themselves be confidential, so review them before
sharing output outside its intended audience. If you need to know which value produced a breach, reproduce it locally
with a debugger rather than printing values into persistent CI output.

## Memory

Series and tag-value identity retained by bounded accounting consists of fixed-size SHA-256 digests, inside the
safety bounds documented in [safety-bounds.md](safety-bounds.md). During a measurement callback, ordinary managed
strings and reusable byte scratch can briefly contain tag-derived identity text. The reusable scratch is cleared on
every digest exit, including short, multi-chunk, empty, and exceptional paths, but ordinary managed process memory
is not a secure-erasure boundary and the package does not promise that transient data is unrecoverable.

## Network

Core verification is offline. It does not open sockets, read files, contact a collector, or require an account.
The only network activity the package can ever cause is the anonymous telemetry described below, which runs on a
background worker and is off by default in environments that opt out.

## Telemetry

KeelMatrix packages use the shared `KeelMatrix.Telemetry` client for a minimal anonymous signal.

**Activation** is one completed budget verification that observed at least one selected instrument. In a fresh
process, that completion requests activation and heartbeat eligibility; the shared client suppresses a duplicate
activation and suppresses a heartbeat in the activation week. Later completed verifications request heartbeat
eligibility, following the shared package cadence of at most one heartbeat per project and ISO week. Installing or
restoring the package, loading the assembly, or constructing a session is not activation.

The field allowlist below is the complete set of aggregate fields this product is **permitted to supply**. It is a
ceiling on what the product could ever attach, not a claim about the payload that is actually transmitted: the
shared client decides what it emits, and it currently emits its own fixed schema with no product-specific fields.

| Field | Value |
| --- | --- |
| `package_version` | the package version, for example `0.1.0` |
| `target_framework` | `net8.0` or `netstandard2.0`, the asset that ran |
| `os_family` | `windows`, `linux`, `macos`, or `other` |
| `observed_instrument_bucket` | `none`, `1-5`, `6-25`, `26-100`, or `101+` |
| `configured_rule_count` | the number of configured rules |
| `outcome` | `pass`, `fail`, or `invalid_configuration` |

Never sent: meter names, instrument names, tag keys, tag values, metric values, URLs or connection information,
application, repository, or project names, stack traces, user-provided budget text, exception messages, and file
paths. The allowlist is a closed internal type with no free-form dictionary, and tests assert that every field the
product supplies is on it and that a hostile workload's names and values cannot appear in the signal.

`KeelMatrix.Telemetry` 0.1.0 exposes activation and heartbeat events with its own fixed schema and no
product-specific fields, and this product's sink forwards only those two calls. The transmitted payload is
therefore the shared schema alone; nothing in the table above is currently sent, and no field can be sent without
being added to this allowlist first.

Telemetry is best effort. Every failure is swallowed, so a telemetry or network failure can never change, delay,
or fail a verification.

## Opting out

Set `KEELMATRIX_NO_TELEMETRY=1`. The shared opt-out set also honors `DOTNET_CLI_TELEMETRY_OPTOUT` and
`DO_NOT_TRACK`, plus repository-local opt-out files. This repository does not track a local telemetry configuration;
an ignored `keelmatrix.telemetry.json` may exist on a developer machine, but it is not part of the repository contract.

This repository opts every local run path out mechanically, so local development, the sample, and the
package-consumer smoke test cannot enter production demand data:

- `tests/KeelMatrix.MetricBudget.Tests/tests.runsettings` sets `KEELMATRIX_NO_TELEMETRY=1` for the test host, and a
  test asserts it, so a missing opt-out fails the suite instead of silently emitting;
- the sample and package-consumer programs set `KEELMATRIX_NO_TELEMETRY=1` before starting a session;
- `scripts/verify-package.ps1` sets the variable for its package, consumer, and sample validation processes;
- `docs/DEV.md` requires the variable for contributor-run development commands, and CI asserts the same setting.

KeelMatrix CI sets and asserts the same variable. The repository ignores local telemetry configuration files and the
pack guard fails closed if one is ever present in a package file list.
