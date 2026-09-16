// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Declares how many distinct values one tag key may produce for one instrument identity within a session.
/// </summary>
/// <remarks>
/// <para>
/// The budget applies to the distinct values observed for that tag key on that instrument identity during the exercised
/// workload. It is not a statement about the values production can produce, and no value here is a safe default
/// for any other codebase.
/// </para>
/// <para>
/// A tag budget is matched against the delivered tag key with ordinal comparison. A measurement that delivers a
/// <see langword="null"/> tag key is observed and reported, but a <see langword="null"/> key cannot be named by a
/// tag budget.
/// </para>
/// <para>
/// Only counts and the tag key are reported. Tag values are never written to the report, to diagnostics, or to
/// telemetry, and only fixed-size digests are retained while a session runs.
/// </para>
/// </remarks>
public sealed class TagBudget
{
    internal TagBudget(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Tag key this budget applies to, exactly as configured.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Maximum number of distinct values this tag key may produce for the instrument identity.
    /// </summary>
    /// <remarks>
    /// The value must be greater than zero. <see langword="null"/> means the tag is declared without a limit, which
    /// is invalid configuration; declare the budget with a limit or remove the tag.
    /// </remarks>
    public int? MaxDistinctValues { get; set; }

    /// <summary>
    /// Returns the configured tag budget in the form <c>"key" &lt;= N distinct values</c>.
    /// </summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        return "\"" + Key + "\" <= "
            + (MaxDistinctValues.HasValue
                ? MaxDistinctValues.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "<unset>")
            + " distinct values";
    }
}
