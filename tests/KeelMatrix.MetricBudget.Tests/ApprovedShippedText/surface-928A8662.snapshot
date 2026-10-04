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

The library adds no product-specific event fields and passes no report data to the shared client. A completed budget
verification requests activation and heartbeat when its report observed at least one selected instrument. An empty or
unmeasured session does not qualify; installing, restoring, loading the assembly, or constructing a session reports
nothing. Each eligible completion requests both events, while the shared client owns repeat-call handling and
heartbeat cadence.

The shared client owns the event schema, opt-out precedence, local storage, endpoint, and retention behavior. Those
details can change independently; see its [privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md).
MetricBudget does not send meter names, instrument names, tag keys or values, metric values, URLs, application or
repository names, exception messages, or file paths as telemetry fields.

Verification results are produced locally and do not depend on telemetry. The shared client's requests are
best-effort, non-blocking, and never throw, so telemetry cannot change, delay, or fail a verification. Bounded
accounting retains only fixed-size digests of series and tag values; transient managed strings and byte buffers can
briefly contain tag-derived data during hashing. Reusable hash scratch is cleared after each digest, including short,
multi-chunk, and empty inputs, but ordinary managed process memory is not a secure-erasure boundary and the package
makes no such promise.

Reports and assertion messages exclude tag values, metric values, and workload samples, but retain application-supplied
meter names, meter versions, instrument names, and tag keys. Those identifiers can themselves be confidential; review
them before sharing output outside its intended audience.

## Opting out

Set `KEELMATRIX_NO_TELEMETRY=1` to opt out. The shared client owns the complete opt-out behavior and precedence; see
its documentation for current controls. Repository-owned development entry points set this variable explicitly;
local telemetry configuration files are ignored and are not tracked.
