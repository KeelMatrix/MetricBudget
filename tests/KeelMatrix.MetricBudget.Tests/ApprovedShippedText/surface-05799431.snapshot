# KeelMatrix.MetricBudget development guide

This page is for repository contributors. It describes local validation and links to development evidence; it is not
package documentation.

## Build and test

Restore and build the solution before package-backed consumers:

```powershell
$env:KEELMATRIX_NO_TELEMETRY = "1"
dotnet restore KeelMatrix.MetricBudget.sln --configfile NuGet.config --force-evaluate --no-cache
dotnet build KeelMatrix.MetricBudget.sln -c Release --no-restore
dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release --framework net8.0 --no-build --no-restore
dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release --framework net472 --no-build --no-restore
dotnet format KeelMatrix.MetricBudget.sln --verify-no-changes --no-restore
```

Run the self-contained package and consumer gate:

```powershell
pwsh -NoProfile -File scripts/verify-package.ps1
```

The solution intentionally excludes those two projects because their `NuGet.config` files map the package under test
to `artifacts/packages`. The package gate performs deterministic archive inspection, clean-cache consumer and sample
proof, and the transitive vulnerability audit.

The environment assignment above is required for repository-owned development and validation runs. Keep it in the
shell that invokes `dotnet`, or use the equivalent process-environment setting on another platform.

## GitHub Actions CI

`.github/workflows/ci.yml` runs for pushes to `main`, pull requests targeting `main`, and manual dispatches. Its
matrix covers `windows-latest` and `ubuntu-latest`, and every job sets `KEELMATRIX_NO_TELEMETRY=1`. Each matrix leg
restores with the committed `NuGet.config`, builds the solution in Release, runs the `net8.0` tests, verifies
formatting, and runs `scripts/verify-package.ps1`. The package gate packs the library, inspects both archives, runs
the clean-cache package-consumer and sample smoke tests, and audits transitive dependencies. Windows additionally
runs the `net472` test host against the `netstandard2.0` asset; that host is not available on Linux.

## Closed documentation guard

The documentation guard compares the complete normalized text of every declared surface with a committed snapshot.
It does not select sentences, parse Markdown regions, or skip code fences, comments, headings, tables, bullets, or
other portions of a surface. Normalization converts line endings to `LF` and removes trailing whitespace from each
line; all remaining text is compared exactly.

The declared repository-text set is reproducible from the repository root with:

```powershell
git ls-files -- '*.md' 'samples/**' 'tests/KeelMatrix.MetricBudget.PackageConsumer/**'
```

The guard interprets every path returned for `*.md` as Markdown. For the `samples/**` and package-consumer
pathspecs, it snapshots every returned UTF-8 text file (a file containing a NUL byte is not text). It then adds the
generated XML documentation for both target frameworks and one runtime snapshot for every outcome branch of
`MetricBudgetReport.ToDiagnosticString()`: `Passed`, `Violation`, `InvalidConfiguration`, `NoMatchingInstrument`,
`NoMeasurementsObserved`, and `ObservationIncomplete`. This is a rule over tracked paths, not a hand-maintained
surface list.

Untracked build output under `bin/` or `obj/` is out of scope by that rule: Git does not return it, and the guard never
scans untracked directories. Such output cannot appear in a Git diff, the source package, or a release. A tracked text
file remains in scope even if its path happens to contain `bin/` or `obj/`.

Snapshots are under `tests/KeelMatrix.MetricBudget.Tests/ApprovedShippedText/`, with one snapshot for each declared
surface. A missing snapshot, an orphan snapshot, a missing tracked file, or a newly tracked text file fails the guard.

Any change to shipped text requires a deliberate approval commit. First review the complete change and confirm that
the scope semantics are still **per instrument identity**, then build Release if generated XML may change and run:

```powershell
$env:KEELMATRIX_METRICBUDGET_APPROVE_DOCUMENTATION_SNAPSHOTS='1'; dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DocumentationScopeTests
```

Inspect the resulting snapshot diff, unset the environment variable, rerun the focused guard, and commit the shipped
text together with its deliberately approved snapshots. Ordinary test runs never regenerate snapshots.

## Development evidence

- [phase0-probe-evidence.md](phase0-probe-evidence.md) records feasibility measurements and is not a consumer guide.
- [testing-internals.md](testing-internals.md) explains the test-only internals access and is not a package contract.
