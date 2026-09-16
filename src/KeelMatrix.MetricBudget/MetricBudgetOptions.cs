// Copyright (c) KeelMatrix

using System.Collections.ObjectModel;
using System.Globalization;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Configures which instruments a session observes, which budgets apply, and how much accounting memory the
/// session may use.
/// </summary>
/// <remarks>
/// <para>
/// Options are read when <see cref="MetricBudgetSession.Start"/> runs and are copied at that moment, so a session
/// keeps the configuration it started with even if the options object is reused or changed later.
/// </para>
/// <para>
/// The session-level limits are safety bounds, not budgets. They exist so that an unexpectedly explosive workload
/// cannot make the verifier itself unbounded, and they are reported explicitly whenever they are reached. They are
/// deliberately not described as safe values for any particular application.
/// </para>
/// </remarks>
public sealed class MetricBudgetOptions
{
    /// <summary>
    /// Default number of distinct observed series a session retains before it reports that series tracking is
    /// incomplete. This is a safety bound, not a budget or a safe cardinality for any application.
    /// </summary>
    public const int DefaultMaxTrackedSeries = 100_000;

    /// <summary>
    /// Default number of distinct values a session retains per tag key before it reports that tag value tracking is
    /// incomplete. This is a safety bound, not a budget or a safe cardinality for any application.
    /// </summary>
    public const int DefaultMaxTrackedValuesPerTag = 5_000;

    /// <summary>
    /// Default length above which a tag value's text is replaced by a stable digest. This is a safety bound, not a
    /// statement about which tag values are acceptable.
    /// </summary>
    public const int DefaultMaxTagValueLength = 256;

    private readonly List<MetricBudgetRule> rules = new();
    private readonly ReadOnlyCollection<MetricBudgetRule> rulesView;

    /// <summary>
    /// Initializes a new set of options with no rules and the default safety bounds.
    /// </summary>
    public MetricBudgetOptions()
    {
        rulesView = new ReadOnlyCollection<MetricBudgetRule>(rules);
    }

    /// <summary>
    /// Maximum number of distinct observed series the session retains for one instrument identity.
    /// </summary>
    /// <remarks>
    /// The bound applies per instrument identity, exactly like <see cref="MaxTrackedValuesPerTag"/>. When one
    /// instrument reaches it, further distinct series of that instrument are counted as untracked observations,
    /// the report says tracking is incomplete, and the session never presents that state as a pass.
    /// <para>
    /// The session does not share one series bound across selected instruments. Its ceiling is the number of
    /// matched instrument identities multiplied by this value, plus the per-tag value sets, so a workload spread
    /// over several instruments retains more series than a single instrument does. Size memory from that product,
    /// and expect <see cref="MetricBudgetOutcome.ObservationIncomplete"/> as soon as any single instrument's bound
    /// is reached.
    /// </para>
    /// <para>
    /// The value must be greater than zero.
    /// </para>
    /// </remarks>
    public int MaxTrackedSeries { get; set; } = DefaultMaxTrackedSeries;

    /// <summary>
    /// Maximum number of distinct values the session retains per tag key and instrument.
    /// </summary>
    /// <remarks>
    /// The value must be greater than zero. When the bound is reached, further distinct values are counted as
    /// untracked observations for that tag key, the report says tracking is incomplete, and the session never
    /// presents that state as a pass.
    /// </remarks>
    public int MaxTrackedValuesPerTag { get; set; } = DefaultMaxTrackedValuesPerTag;

    /// <summary>
    /// Maximum length of a tag value's invariant text before the value is represented by a stable digest.
    /// </summary>
    /// <remarks>
    /// The bound keeps one pathological value from inflating an identity. The digested form still distinguishes
    /// different values, and the digest is never a raw value. The value must be greater than zero.
    /// </remarks>
    public int MaxTagValueLength { get; set; } = DefaultMaxTagValueLength;

    /// <summary>
    /// Instrument-selection rules and their budgets, in declaration order.
    /// </summary>
    public IReadOnlyList<MetricBudgetRule> Rules => rulesView;

    /// <summary>
    /// Adds a rule for one instrument name in any meter.
    /// </summary>
    /// <param name="instrumentName">Exact instrument name.</param>
    /// <param name="configure">Configures the budget for the selected instruments.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="instrumentName"/> is null, empty, or white space.</exception>
    public MetricBudgetOptions ForInstrument(string instrumentName, Action<InstrumentBudget> configure)
    {
        return ForInstrument(InstrumentSelector.Instrument(instrumentName), configure);
    }

    /// <summary>
    /// Adds a rule for one instrument name in one meter.
    /// </summary>
    /// <param name="meterName">Exact meter name.</param>
    /// <param name="instrumentName">Exact instrument name.</param>
    /// <param name="configure">Configures the budget for the selected instruments.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException">A name is null, empty, or white space.</exception>
    public MetricBudgetOptions ForInstrument(string meterName, string instrumentName, Action<InstrumentBudget> configure)
    {
        return ForInstrument(InstrumentSelector.InstrumentInMeter(meterName, instrumentName), configure);
    }

    /// <summary>
    /// Adds a rule for every instrument published by one meter.
    /// </summary>
    /// <param name="meterName">Exact meter name.</param>
    /// <param name="configure">Configures the budget for the selected instruments.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="meterName"/> is null, empty, or white space.</exception>
    public MetricBudgetOptions ForMeter(string meterName, Action<InstrumentBudget> configure)
    {
        return ForInstrument(InstrumentSelector.Meter(meterName), configure);
    }

    /// <summary>
    /// Adds a rule for a selector that was built directly.
    /// </summary>
    /// <param name="selector">Selector that describes the instruments to observe.</param>
    /// <param name="configure">Configures the budget for the selected instruments.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public MetricBudgetOptions ForInstrument(InstrumentSelector selector, Action<InstrumentBudget> configure)
    {
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        InstrumentBudget budget = new InstrumentBudget();
        configure(budget);
        rules.Add(new MetricBudgetRule(selector, budget));
        return this;
    }

    /// <summary>
    /// Validates these options and copies them into the immutable form a session uses.
    /// </summary>
    /// <returns>The frozen configuration.</returns>
    /// <exception cref="MetricBudgetConfigurationException">The options cannot describe a verification.</exception>
    internal FrozenOptions Freeze()
    {
        List<string> problems = new List<string>();

        if (rules.Count == 0)
        {
            problems.Add("No instrument selection rule is configured. Add at least one ForInstrument or ForMeter rule.");
        }

        ValidatePositive(MaxTrackedSeries, nameof(MaxTrackedSeries), problems);
        ValidatePositive(MaxTrackedValuesPerTag, nameof(MaxTrackedValuesPerTag), problems);
        ValidatePositive(MaxTagValueLength, nameof(MaxTagValueLength), problems);

        FrozenRule[] frozenRules = new FrozenRule[rules.Count];
        for (int i = 0; i < rules.Count; i++)
        {
            MetricBudgetRule rule = rules[i];
            string prefix = "Rule " + (i + 1).ToString(CultureInfo.InvariantCulture) + " (" + rule.Selector + "): ";

            if (!rule.Budget.HasLimit())
            {
                problems.Add(prefix + "no budget is configured. Set MaxObservedSeries or declare at least one tag budget with MaxDistinctValues.");
            }

            if (rule.Budget.MaxObservedSeries is int maxSeries)
            {
                if (maxSeries <= 0)
                {
                    problems.Add(prefix + "MaxObservedSeries must be greater than zero.");
                }
            }

            foreach (TagBudget tag in rule.Budget.Tags)
            {
                if (!tag.MaxDistinctValues.HasValue)
                {
                    problems.Add(prefix + "tag \"" + tag.Key + "\" has no MaxDistinctValues. Set a limit or remove the tag.");
                }
                else if (tag.MaxDistinctValues.Value <= 0)
                {
                    problems.Add(prefix + "tag \"" + tag.Key + "\" MaxDistinctValues must be greater than zero.");
                }
            }

            frozenRules[i] = FrozenRule.Create(rule);
        }

        if (problems.Count > 0)
        {
            throw new MetricBudgetConfigurationException(
                "Invalid MetricBudget configuration: " + string.Join(" ", problems));
        }

        return new FrozenOptions(MaxTrackedSeries, MaxTrackedValuesPerTag, MaxTagValueLength, frozenRules);
    }

    private static void ValidatePositive(int value, string name, List<string> problems)
    {
        if (value <= 0)
        {
            problems.Add(name + " must be greater than zero because it is a hard safety bound.");
        }
    }
}
