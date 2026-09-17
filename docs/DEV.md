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
Use `dotnet format --verify-no-changes` and
`dotnet list KeelMatrix.MetricBudget.sln package --vulnerable --include-transitive` for the remaining repository gates.

## Closed documentation guard

The bound-wording guard is closed by design. It approves only exact, whitespace-normalized units listed in
`tests/KeelMatrix.MetricBudget.Tests/DocumentationScopeInventory.txt`; it does not infer correctness from verbs,
adjectives, nouns, negation, or other wording patterns. A new or reworded bound statement therefore fails until it is
deliberately approved.

To approve a statement, first decide that the exact shipped unit is correct, then add one tab-separated
`<surface><TAB><normalized unit>` entry to the inventory, update that surface's explicit floor in
`DocumentationScopeTests.cs`, and rerun the focused `DocumentationScopeTests` for both test target frameworks. The
floor requires every currently approved identity to remain present; change it only when the surface contract is
deliberately changing. The test owns the surface declarations, including recursive/glob expansions, both XML
documentation files, and the runtime diagnostic; a missing declared surface fails closed.

## Development evidence

- [phase0-probe-evidence.md](phase0-probe-evidence.md) records feasibility measurements and is not a consumer guide.
- [testing-internals.md](testing-internals.md) explains the test-only internals access and is not a package contract.
