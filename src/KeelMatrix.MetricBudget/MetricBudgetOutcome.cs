// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// The result of one completed budget verification.
/// </summary>
/// <remarks>
/// Only <see cref="Passed"/> means the exercised workload stayed within every configured budget. The other values
/// name why the session could not reach that conclusion, so a session never passes merely because it observed
/// nothing.
/// </remarks>
public enum MetricBudgetOutcome
{
    /// <summary>
    /// Every configured rule selected at least one instrument, every selected instrument delivered at least one
    /// measurement, all accounting was complete, and no configured budget was exceeded.
    /// </summary>
    Passed = 0,

    /// <summary>
    /// At least one configured budget was exceeded by the observed workload.
    /// </summary>
    Violation = 1,

    /// <summary>
    /// The configuration could not be applied, for example because one instrument identity matched more than one
    /// rule. Ambiguous instruments are not observed, and their measurements never silently pick a budget.
    /// </summary>
    InvalidConfiguration = 2,

    /// <summary>
    /// At least one configured rule selected no published instrument. Common causes are a misspelled meter or
    /// instrument name, a workload that does not exercise the instrument, or an instrument created after the
    /// session completed.
    /// </summary>
    NoMatchingInstrument = 3,

    /// <summary>
    /// Every configured rule selected an instrument, but at least one selected instrument delivered no
    /// measurements during the session.
    /// </summary>
    NoMeasurementsObserved = 4,

    /// <summary>
    /// Accounting could not retain all observations or state within the configured safety bounds, or the session
    /// detected an impossible accounting state. A selector conflict or proven budget breach takes precedence over
    /// this outcome; inspect the report's completeness properties and accounting flag as well.
    /// </summary>
    ObservationIncomplete = 5,
}
