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

Complete retained instrument identities also expose an `IdentityDiscriminator`, a lowercase SHA-256 token derived from
the identity fields. It distinguishes same-name streams without exposing unit, description, measurement type, meter
tags, or instrument tags. Static-metadata rejection records contain only the rejected dimension, count, and effective
bound; they never contain metadata values.

## Memory

Series and tag-value identity retained by bounded accounting consists of fixed-size SHA-256 digests, inside the
safety bounds documented in [safety-bounds.md](safety-bounds.md). During a measurement callback, ordinary managed
strings and reusable byte scratch can briefly contain tag-derived identity text. The reusable scratch is cleared on
every digest exit, including short, multi-chunk, empty, and exceptional paths, but ordinary managed process memory
is not a secure-erasure boundary and the package does not promise that transient data is unrecoverable.

## Network

Core verification is offline. It does not open sockets, read files, contact a collector, or require an account.
MetricBudget can request the shared anonymous telemetry client after an eligible completed verification. The shared
package owns network delivery and opt-out behavior.

## Telemetry

KeelMatrix packages use the shared `KeelMatrix.Telemetry` client for a minimal anonymous signal.

**Activation eligibility** is product-specific: a completed budget verification qualifies when its report observed
at least one selected instrument. An empty or unmeasured session does not qualify. Installing or restoring the
package, loading the assembly, or constructing a session is not activation. A qualifying completion requests both
activation and heartbeat from the shared client; every qualifying completion makes the same requests. The shared
client owns duplicate suppression, heartbeat cadence, identity, state, queueing, delivery, and opt-out behavior.

MetricBudget adds no event fields and passes no report data to the shared client. Meter names, instrument names, tag
keys or values, metric values, URLs, application or repository names, exception messages, and file paths are not
sent by this package. The shared client owns the emitted event schema and controls; see its [README](https://github.com/KeelMatrix/Telemetry#readme)
and [privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for current implementation
details.

The shared client's activation and heartbeat requests are best-effort, non-blocking, and never throw. Verification
results do not depend on telemetry, so telemetry cannot change, delay, or fail a verification.

## Opting out

Set `KEELMATRIX_NO_TELEMETRY=1` to opt out. The shared client owns the complete opt-out behavior and precedence; see
its documentation for current controls. This repository does not track a local telemetry configuration; an ignored
`keelmatrix.telemetry.json` may exist on a developer machine, but it is not part of the repository contract.

This repository opts every local run path out mechanically, so local development, the sample, and the
package-consumer smoke test cannot enter production demand data:

- `tests/KeelMatrix.MetricBudget.Tests/tests.runsettings` sets `KEELMATRIX_NO_TELEMETRY=1` for the test host, and a
  test asserts it, so a missing opt-out fails the suite instead of silently emitting;
- the sample and package-consumer programs set `KEELMATRIX_NO_TELEMETRY=1` before starting a session;
- `scripts/verify-package.ps1` sets the variable for its package, consumer, and sample validation processes;
- `docs/DEV.md` requires the variable for contributor-run development commands, and CI asserts the same setting.

KeelMatrix CI sets and asserts the same variable. The repository ignores local telemetry configuration files and the
pack guard fails closed if one is ever present in a package file list.
