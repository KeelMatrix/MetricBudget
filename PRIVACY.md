# Privacy

`KeelMatrix.MetricBudget` uses `KeelMatrix.Telemetry` transitively for minimal anonymous usage telemetry.

This repository is not the source of truth for telemetry implementation details such as:

- event types
- emitted fields
- opt-out environment variables
- local storage layout
- network endpoint
- retention behavior

Those details can change with the telemetry package and are maintained in the telemetry repository:

- Telemetry README: <https://github.com/KeelMatrix/Telemetry#readme>
- Telemetry privacy policy: <https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md>

## Product-specific note

The library uses the shared activation and heartbeat events and adds no product-specific fields. A completed budget
verification that observed at least one selected instrument requests activation and heartbeat eligibility in a fresh
process; the shared client suppresses duplicate activation and same-week heartbeat events. Later completions request
heartbeat eligibility. Installing the package, restoring it, loading the assembly, or constructing a session reports
nothing.

The only aggregate values this product models are documented in
[docs/privacy-and-telemetry.md](docs/privacy-and-telemetry.md): package version, target framework, coarse OS
family, a coarse observed-instrument bucket, the configured-rule count, and a coarse outcome. Meter names,
instrument names, tag keys, tag values, metric values, URLs, application or repository names, exception messages,
and file paths are never emitted.

Verification results are produced locally and never depend on telemetry. A telemetry or network failure cannot
change, delay, or fail a verification.

## Opting out

Set `KEELMATRIX_NO_TELEMETRY=1`. The shared opt-out set also honors `DOTNET_CLI_TELEMETRY_OPTOUT` and
`DO_NOT_TRACK`, plus repo-local opt-out configuration files.
