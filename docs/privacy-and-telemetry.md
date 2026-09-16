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

Reports and assertion messages are therefore safe to paste into an issue or leave in CI logs. If you need to know
which value produced a breach, reproduce it locally with a debugger rather than printing values into persistent
CI output.

## Memory

Series and tag-value identity are retained as fixed-size SHA-256 digests, inside the safety bounds documented in
[safety-bounds.md](safety-bounds.md). The canonical identity text that contains values is transient: it is built
during the measurement callback, hashed, and released.

## Network

Core verification is offline. It does not open sockets, read files, contact a collector, or require an account.
The only network activity the package can ever cause is the anonymous telemetry described below, which runs on a
background worker and is off by default in environments that opt out.

## Telemetry

KeelMatrix packages use the shared `KeelMatrix.Telemetry` client for a minimal anonymous signal.

**Activation** is one completed budget verification that observed at least one selected instrument. Installing or
restoring the package, loading the assembly, or constructing a session is not activation. Later completed
verifications request a low-frequency heartbeat, following the shared package cadence.

The complete field allowlist for this product is:

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
paths. The allowlist is a closed internal type with no free-form dictionary, and tests assert that every field is
on it and that a hostile workload's names and values cannot appear in the signal.

`KeelMatrix.Telemetry` 0.1.0 exposes activation and heartbeat events with its own fixed schema and no
product-specific fields, so this allowlist currently documents the only aggregate fields this product would ever
attach; the transmitted payload is the shared schema alone.

Telemetry is best effort. Every failure is swallowed, so a telemetry or network failure can never change, delay,
or fail a verification.

## Opting out

Set `KEELMATRIX_NO_TELEMETRY=1`. The shared opt-out set also honors `DOTNET_CLI_TELEMETRY_OPTOUT` and
`DO_NOT_TRACK`, plus repo-local opt-out configuration files. KeelMatrix development and KeelMatrix CI run with
telemetry opted out, and this repository's own test host sets the opt-out variable so the suite can never emit
production demand data.
