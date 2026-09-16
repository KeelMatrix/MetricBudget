// Copyright (c) KeelMatrix

using System.Runtime.CompilerServices;

// The behavior tests assert internal accounting invariants (for example, that an impossible account fails loudly)
// in addition to the public report surface. The attribute is intentionally part of the shipped assembly so that
// Release builds - the configuration the whole gate runs in - keep those assertions compiled and honest; a
// Debug-only grant would silently move them to a configuration nobody verifies. It exposes internals to an
// assembly that claims this exact name and nothing else: it selects no public surface, and it is not a trust
// boundary. Trimming it before release would require either a second per-target-framework compile of the library
// or reflection-based tests, and neither is worth weakening or complicating this gate.
[assembly: InternalsVisibleTo("KeelMatrix.MetricBudget.Tests")]
