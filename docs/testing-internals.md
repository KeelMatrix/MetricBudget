# Testing internals

The library's behavior tests assert internal accounting invariants in addition to the public report surface. The
tests therefore run against the same Release library build that is packaged, rather than relying on a Debug-only
test path.

`KeelMatrix.MetricBudget` grants internals access only to the assembly with the exact name
`KeelMatrix.MetricBudget.Tests`. This exposes no public API, selects no other assembly, and is not a trust boundary.
Keeping the grant in the Release build means the tests continue to verify the production configuration's accounting
invariants. Removing the grant would require either a second per-target-framework library build or reflection-based
tests, neither of which would strengthen the shipped contract.
