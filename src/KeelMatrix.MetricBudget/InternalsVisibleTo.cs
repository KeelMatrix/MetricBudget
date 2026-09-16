// Copyright (c) KeelMatrix

using System.Runtime.CompilerServices;

// The behavior tests assert internal accounting invariants (for example, that an impossible account fails loudly)
// in addition to the public report surface.
[assembly: InternalsVisibleTo("KeelMatrix.MetricBudget.Tests")]
