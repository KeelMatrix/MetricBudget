# KeelMatrix.MetricBudget development guide

This page is for repository contributors. It describes local validation and links to development evidence; it is not
package documentation.

## Build and test

Restore and build the solution before package-backed consumers:

```powershell
dotnet restore KeelMatrix.MetricBudget.sln
dotnet build KeelMatrix.MetricBudget.sln -c Release --no-restore
dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release --no-build --framework net8.0
dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release --no-build --framework net472
```

Create the local package, then run both package-backed projects:

```powershell
dotnet pack src/KeelMatrix.MetricBudget/KeelMatrix.MetricBudget.csproj -c Release -o artifacts/packages --no-build
dotnet run --project tests/KeelMatrix.MetricBudget.PackageConsumer -c Release
dotnet run --project samples/KeelMatrix.MetricBudget.Sample -c Release
```

The solution intentionally excludes those two projects because their `NuGet.config` files map the package under test
to `artifacts/packages`. Run `scripts/verify-package.ps1` for deterministic archive inspection and clean-cache proof.
Use `dotnet format KeelMatrix.MetricBudget.sln --verify-no-changes` and
`dotnet list KeelMatrix.MetricBudget.sln package --vulnerable --include-transitive` for the remaining repository gates.

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
$env:KEELMATRIX_METRICBUDGET_APPROVE_DOCUMENTATION_SNAPSHOTS='1'; dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj -c Release --no-build --filter FullyQualifiedName~DocumentationScopeTests
```

Inspect the resulting snapshot diff, unset the environment variable, rerun the focused guard, and commit the shipped
text together with its deliberately approved snapshots. Ordinary test runs never regenerate snapshots.

## Development evidence

- [phase0-probe-evidence.md](phase0-probe-evidence.md) records feasibility measurements and is not a consumer guide.
- [testing-internals.md](testing-internals.md) explains the test-only internals access and is not a package contract.
