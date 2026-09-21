// Copyright (c) KeelMatrix

using System.Globalization;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Turns a completed session snapshot into the immutable public report.
/// </summary>
/// <remarks>
/// The builder is the single place that decides the outcome ladder, so "nothing was verified" can never be
/// reported as a pass:
/// <list type="number">
/// <item>invalid configuration, when an instrument identity matched more than one rule;</item>
/// <item>violation, when a tracked budget was exceeded;</item>
/// <item>observation incomplete, when a safety bound was reached or accounting was inconsistent;</item>
/// <item>no matching instrument, when a rule selected nothing;</item>
/// <item>no measurements observed, when a selected instrument delivered nothing;</item>
/// <item>pass.</item>
/// </list>
/// </remarks>
internal static class ReportBuilder
{
    internal static MetricBudgetReport Build(FrozenOptions options, SessionSnapshot snapshot)
    {
        List<string> invariantProblems = new List<string>();
        bool accountingIsConsistent = AccountingInvariants.Validate(snapshot, invariantProblems);

        List<MetricBudgetViolation> violations = new List<MetricBudgetViolation>();
        List<MetricBudgetRuleResult> ruleResults = new List<MetricBudgetRuleResult>(options.Rules.Length);

        bool anyRuleUnmatched = false;
        bool anyInstrumentUnmeasured = false;
        bool anyBudgetExceeded = false;

        long untrackedTagValueObservations = 0;

        for (int i = 0; i < options.Rules.Length; i++)
        {
            FrozenRule rule = options.Rules[i];

            List<InstrumentAccountSnapshot> matched = new List<InstrumentAccountSnapshot>();
            for (int j = 0; j < snapshot.Instruments.Length; j++)
            {
                if (snapshot.Instruments[j].RuleIndex == i)
                {
                    matched.Add(snapshot.Instruments[j]);
                }
            }

            bool ruleHasMeasurements = false;
            List<MetricBudgetInstrumentResult> instruments = new List<MetricBudgetInstrumentResult>(matched.Count);

            for (int j = 0; j < matched.Count; j++)
            {
                InstrumentAccountSnapshot account = matched[j];
                untrackedTagValueObservations += account.Tags.Sum(static tag => tag.UntrackedValueObservations);

                List<MetricBudgetTagResult> tags = BuildTagResults(rule, account);
                MetricBudgetInstrumentResult instrument = new MetricBudgetInstrumentResult(
                    account.Identity.MeterName,
                    account.Identity.MeterVersion,
                    account.Identity.InstrumentName,
                    account.Identity.Kind,
                    account.MeasurementCount,
                    account.ObservedSeriesCount,
                    rule.MaxObservedSeries,
                    account.UntrackedSeriesObservations,
                    account.SeriesTrackingIncomplete,
                    account.UntrackedTagSetObservations,
                    account.UntrackedTagKeyObservations,
                    account.TagSetTrackingIncomplete,
                    account.TagKeyCapExhausted,
                    account.InstrumentTrackingIncomplete,
                    tags);

                instruments.Add(instrument);

                if (account.MeasurementCount == 0)
                {
                    anyInstrumentUnmeasured = true;
                    violations.Add(new MetricBudgetViolation(
                        MetricBudgetViolationKind.InstrumentProducedNoMeasurements,
                        "selected instrument " + account.Identity.Describe()
                        + " was published but delivered no measurements during this session, so nothing was "
                        + "verified for it. Exercise the instrument in the workload, or call "
                        + "RecordObservableInstruments for observable instruments.",
                        account.Identity.MeterName,
                        account.Identity.MeterVersion,
                        account.Identity.InstrumentName,
                        account.Identity.Kind,
                        tagKey: null,
                        observedCount: 0,
                        configuredLimit: null));
                    continue;
                }

                ruleHasMeasurements = true;

                if (account.TagSetTrackingIncomplete)
                {
                    violations.Add(new MetricBudgetViolation(
                        MetricBudgetViolationKind.SafetyLimitReached,
                        "one or more delivered tag sets for " + account.Identity.Describe()
                        + " could not be tracked because a tag set was oversized or contained an unsupported value."
                        + " The observed series count is a lower bound; use only supported tag value types and keep"
                        + " tag keys and tag sets within the configured safety bounds.",
                        account.Identity.MeterName,
                        account.Identity.MeterVersion,
                        account.Identity.InstrumentName,
                        account.Identity.Kind,
                        tagKey: null,
                        observedCount: null,
                        configuredLimit: null));
                }

                if (account.TagKeyCapExhausted)
                {
                    violations.Add(new MetricBudgetViolation(
                        MetricBudgetViolationKind.SafetyLimitReached,
                        "the retained tag-key safety bound for " + account.Identity.Describe()
                        + " was reached; additional delivered keys were not tracked and the observed tag state is a"
                        + " lower bound. Raise MaxTrackedTagKeysPerInstrument or narrow the workload.",
                        account.Identity.MeterName,
                        account.Identity.MeterVersion,
                        account.Identity.InstrumentName,
                        account.Identity.Kind,
                        tagKey: null,
                        observedCount: null,
                        configuredLimit: null));
                }

                if (rule.MaxObservedSeries is int maxSeries && account.ObservedSeriesCount > maxSeries)
                {
                    anyBudgetExceeded = true;
                    violations.Add(new MetricBudgetViolation(
                        MetricBudgetViolationKind.ObservedSeriesBudgetExceeded,
                        "observed series budget exceeded by " + account.Identity.Describe()
                        + ": " + account.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture)
                        + " observed series against a configured maximum of "
                        + maxSeries.ToString(CultureInfo.InvariantCulture)
                        + ". Reduce tag fan-out on the exercised path, or raise MaxObservedSeries when the workload "
                        + "is representative of production.",
                        account.Identity.MeterName,
                        account.Identity.MeterVersion,
                        account.Identity.InstrumentName,
                        account.Identity.Kind,
                        tagKey: null,
                        observedCount: account.ObservedSeriesCount,
                        configuredLimit: maxSeries));
                }

                for (int k = 0; k < tags.Count; k++)
                {
                    MetricBudgetTagResult tag = tags[k];
                    if (tag.ValueTrackingIncomplete)
                    {
                        violations.Add(new MetricBudgetViolation(
                            MetricBudgetViolationKind.SafetyLimitReached,
                            "tag value safety bound for the instrument identity reached for tag " + DescribeTagKey(tag.Key) + " on "
                            + account.Identity.Describe()
                            + ": " + tag.ObservedDistinctValueCount.ToString(CultureInfo.InvariantCulture)
                            + " distinct values were retained and "
                            + tag.UntrackedValueObservations.ToString(CultureInfo.InvariantCulture)
                            + " measurement(s) could not be tracked. The count is a lower bound; raise "
                            + "MaxTrackedValuesPerTag or narrow the workload.",
                            account.Identity.MeterName,
                            account.Identity.MeterVersion,
                            account.Identity.InstrumentName,
                            account.Identity.Kind,
                            tag.Key,
                            tag.ObservedDistinctValueCount,
                            configuredLimit: null));
                    }

                    if (tag.IsConfigured
                        && tag.ConfiguredMaxDistinctValues is int tagLimit
                        && tag.ObservedDistinctValueCount > tagLimit)
                    {
                        anyBudgetExceeded = true;
                        violations.Add(new MetricBudgetViolation(
                            MetricBudgetViolationKind.TagDistinctValuesBudgetExceeded,
                            "tag distinct-value budget exceeded by " + account.Identity.Describe()
                            + " for tag " + DescribeTagKey(tag.Key) + ": "
                            + tag.ObservedDistinctValueCount.ToString(CultureInfo.InvariantCulture)
                            + " observed distinct values against a configured maximum of "
                            + tagLimit.ToString(CultureInfo.InvariantCulture)
                            + ". Constrain the values this tag can carry, or raise MaxDistinctValues when the "
                            + "workload is representative of production.",
                            account.Identity.MeterName,
                            account.Identity.MeterVersion,
                            account.Identity.InstrumentName,
                            account.Identity.Kind,
                            tag.Key,
                            tag.ObservedDistinctValueCount,
                            tagLimit));
                    }
                }
            }

            InstrumentObservationState state = matched.Count == 0
                ? InstrumentObservationState.NotObserved
                : ruleHasMeasurements
                    ? InstrumentObservationState.Observed
                    : InstrumentObservationState.NoMeasurements;

            if (state == InstrumentObservationState.NotObserved)
            {
                anyRuleUnmatched = true;
                violations.Add(new MetricBudgetViolation(
                    MetricBudgetViolationKind.InstrumentNotObserved,
                    "rule " + (i + 1).ToString(CultureInfo.InvariantCulture) + " (" + rule.Selector
                    + ") matched no published instrument, so nothing was verified for it. Check that the workload "
                    + "publishes an instrument with that exact name, that the meter is still alive, and that the "
                    + "session is started before the workload runs. Names are matched exactly and are "
                    + "case-sensitive.",
                    meterName: rule.Selector.MeterName,
                    meterVersion: null,
                    instrumentName: rule.Selector.InstrumentName,
                    instrumentKind: MetricInstrumentKind.Unknown,
                    tagKey: null,
                    observedCount: 0,
                    configuredLimit: null));
            }

            ruleResults.Add(new MetricBudgetRuleResult(
                new MetricBudgetRule(rule.Selector, OriginalBudget(rule)),
                i,
                state,
                instruments));
        }

        bool seriesTrackingIncomplete = snapshot.SeriesTrackingIncomplete;
        bool tagValueTrackingIncomplete = snapshot.TagValueTrackingIncomplete;

        if (snapshot.InstrumentIdentityTrackingIncomplete)
        {
            violations.Add(new MetricBudgetViolation(
                MetricBudgetViolationKind.SafetyLimitReached,
                "the instrument-identity safety bound was reached; "
                + snapshot.UntrackedInstrumentIdentities.ToString(CultureInfo.InvariantCulture)
                + " selected published identity(ies) were not retained or enabled. The report is incomplete; raise "
                + "MaxTrackedInstrumentIdentities or narrow the workload.",
                meterName: null,
                meterVersion: null,
                instrumentName: null,
                instrumentKind: MetricInstrumentKind.Unknown,
                tagKey: null,
                observedCount: null,
                configuredLimit: snapshot.MaxTrackedInstrumentIdentities));
        }

        if (snapshot.InstrumentInstanceTrackingIncomplete)
        {
            violations.Add(new MetricBudgetViolation(
                MetricBudgetViolationKind.SafetyLimitReached,
                "the physical-instrument safety bound was reached; "
                + snapshot.UntrackedInstrumentInstances.ToString(CultureInfo.InvariantCulture)
                + " selected instance(s) were not retained or enabled. The report is incomplete; raise "
                + "MaxTrackedInstrumentInstances or narrow the workload.",
                meterName: null,
                meterVersion: null,
                instrumentName: null,
                instrumentKind: MetricInstrumentKind.Unknown,
                tagKey: null,
                observedCount: null,
                configuredLimit: snapshot.MaxTrackedInstrumentInstances));
        }

        if (snapshot.InstrumentIdentityLengthTrackingIncomplete)
        {
            violations.Add(new MetricBudgetViolation(
                MetricBudgetViolationKind.SafetyLimitReached,
                "the instrument identity length safety bound was reached; "
                + snapshot.UntrackedInstrumentIdentityLengths.ToString(CultureInfo.InvariantCulture)
                + " selected published identity(ies) were not retained or enabled because a meter name, meter version, "
                + "or instrument name exceeded MaxInstrumentIdentityLength. The report is incomplete; raise "
                + "MaxInstrumentIdentityLength or narrow the workload.",
                meterName: null,
                meterVersion: null,
                instrumentName: null,
                instrumentKind: MetricInstrumentKind.Unknown,
                tagKey: null,
                observedCount: null,
                configuredLimit: snapshot.MaxInstrumentIdentityLength));
        }

        if (snapshot.ConflictTrackingIncomplete)
        {
            violations.Add(new MetricBudgetViolation(
                MetricBudgetViolationKind.SafetyLimitReached,
                "the configuration-conflict safety bound was reached; "
                + snapshot.UntrackedConflicts.ToString(CultureInfo.InvariantCulture)
                + " ambiguous identity record(s) were not retained. The report is incomplete; raise "
                + "MaxTrackedConflicts or narrow the workload.",
                meterName: null,
                meterVersion: null,
                instrumentName: null,
                instrumentKind: MetricInstrumentKind.Unknown,
                tagKey: null,
                observedCount: null,
                configuredLimit: snapshot.MaxTrackedConflicts));
        }

        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            InstrumentAccountSnapshot account = snapshot.Instruments[i];
            if (!account.SeriesTrackingIncomplete)
            {
                continue;
            }

            violations.Add(new MetricBudgetViolation(
                MetricBudgetViolationKind.SafetyLimitReached,
                "series safety bound for the instrument identity reached while observing " + account.Identity.Describe()
                + ": " + account.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture)
                + " distinct observed series were retained and "
                + account.UntrackedSeriesObservations.ToString(CultureInfo.InvariantCulture)
                + " measurement(s) could not be tracked. Observed per-instrument-identity series counts are lower bounds; raise "
                + "per-instrument-identity MaxTrackedSeries or narrow the workload.",
                account.Identity.MeterName,
                account.Identity.MeterVersion,
                account.Identity.InstrumentName,
                account.Identity.Kind,
                tagKey: null,
                observedCount: account.ObservedSeriesCount,
                configuredLimit: snapshot.MaxTrackedSeries));
        }

        for (int i = 0; i < snapshot.Conflicts.Length; i++)
        {
            ConfigurationConflictSnapshot conflict = snapshot.Conflicts[i];
            violations.Add(new MetricBudgetViolation(
                MetricBudgetViolationKind.ConfigurationInvalid,
                "instrument " + conflict.Identity.Describe() + " matched rules "
                + string.Join(", ", conflict.RuleIndexes.Select(static index => (index + 1).ToString(CultureInfo.InvariantCulture)))
                + ", so no budget can be applied to it and it was not observed. Make the rules disjoint.",
                conflict.Identity.MeterName,
                conflict.Identity.MeterVersion,
                conflict.Identity.InstrumentName,
                conflict.Identity.Kind,
                tagKey: null,
                observedCount: null,
                configuredLimit: null));
        }

        for (int i = 0; i < invariantProblems.Count; i++)
        {
            violations.Add(new MetricBudgetViolation(
                MetricBudgetViolationKind.AccountingInconsistent,
                invariantProblems[i],
                meterName: null,
                meterVersion: null,
                instrumentName: null,
                instrumentKind: MetricInstrumentKind.Unknown,
                tagKey: null,
                observedCount: null,
                configuredLimit: null));
        }

        MetricBudgetOutcome outcome = DetermineOutcome(
            snapshot,
            anyBudgetExceeded,
            anyRuleUnmatched,
            anyInstrumentUnmeasured,
            accountingIsConsistent,
            seriesTrackingIncomplete,
            tagValueTrackingIncomplete,
            snapshot.StateTrackingIncomplete);

        MetricBudgetSafetyReport safety = new MetricBudgetSafetyReport(
            snapshot.MaxTrackedSeries,
            snapshot.MaxTrackedValuesPerTag,
            snapshot.MaxTagValueLength,
            seriesTrackingIncomplete,
            UntrackedSeriesObservations(snapshot),
            tagValueTrackingIncomplete,
            untrackedTagValueObservations,
            snapshot.MaxTrackedInstrumentIdentities,
            snapshot.MaxTrackedInstrumentInstances,
            snapshot.MaxTrackedConflicts,
            snapshot.MaxTrackedTagKeysPerInstrument,
            snapshot.MaxTagCount,
            snapshot.MaxInstrumentIdentityLength,
            snapshot.MaxTagKeyLength,
            snapshot.InstrumentIdentityTrackingIncomplete
                || snapshot.InstrumentInstanceTrackingIncomplete
                || snapshot.InstrumentIdentityLengthTrackingIncomplete,
            snapshot.UntrackedInstrumentIdentities,
            snapshot.UntrackedInstrumentInstances,
            snapshot.InstrumentIdentityLengthTrackingIncomplete,
            snapshot.UntrackedInstrumentIdentityLengths,
            snapshot.ConflictTrackingIncomplete,
            snapshot.UntrackedConflicts,
            TagSetTrackingIncomplete(snapshot),
            UntrackedTagSetObservations(snapshot),
            TagKeyTrackingIncomplete(snapshot),
            UntrackedTagKeyObservations(snapshot));

        int observedInstrumentCount = 0;
        int observedSeriesCount = 0;
        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            if (snapshot.Instruments[i].MeasurementCount > 0)
            {
                observedInstrumentCount++;
            }

            observedSeriesCount += snapshot.Instruments[i].ObservedSeriesCount;
        }

        return new MetricBudgetReport(
            outcome,
            ruleResults,
            violations,
            safety,
            snapshot.AccountedMeasurements,
            observedInstrumentCount,
            observedSeriesCount,
            accountingIsConsistent);
    }

    private static MetricBudgetOutcome DetermineOutcome(
        SessionSnapshot snapshot,
        bool anyBudgetExceeded,
        bool anyRuleUnmatched,
        bool anyInstrumentUnmeasured,
        bool accountingIsConsistent,
        bool seriesTrackingIncomplete,
        bool tagValueTrackingIncomplete,
        bool stateTrackingIncomplete)
    {
        if (snapshot.Conflicts.Length > 0)
        {
            return MetricBudgetOutcome.InvalidConfiguration;
        }

        if (anyBudgetExceeded)
        {
            return MetricBudgetOutcome.Violation;
        }

        if (!accountingIsConsistent || seriesTrackingIncomplete || tagValueTrackingIncomplete || stateTrackingIncomplete)
        {
            return MetricBudgetOutcome.ObservationIncomplete;
        }

        if (anyRuleUnmatched)
        {
            return MetricBudgetOutcome.NoMatchingInstrument;
        }

        if (anyInstrumentUnmeasured)
        {
            return MetricBudgetOutcome.NoMeasurementsObserved;
        }

        return MetricBudgetOutcome.Passed;
    }

    private static long UntrackedSeriesObservations(SessionSnapshot snapshot)
    {
        long total = 0;
        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            total += snapshot.Instruments[i].UntrackedSeriesObservations;
        }

        return total;
    }

    private static long UntrackedTagSetObservations(SessionSnapshot snapshot)
    {
        long total = 0;
        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            total += snapshot.Instruments[i].UntrackedTagSetObservations;
        }

        return total;
    }

    private static long UntrackedTagKeyObservations(SessionSnapshot snapshot)
    {
        long total = 0;
        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            total += snapshot.Instruments[i].UntrackedTagKeyObservations;
        }

        return total;
    }

    private static bool TagSetTrackingIncomplete(SessionSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            if (snapshot.Instruments[i].TagSetTrackingIncomplete)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TagKeyTrackingIncomplete(SessionSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            if (snapshot.Instruments[i].TagKeyCapExhausted)
            {
                return true;
            }
        }

        return false;
    }

    private static List<MetricBudgetTagResult> BuildTagResults(FrozenRule rule, InstrumentAccountSnapshot account)
    {
        List<MetricBudgetTagResult> results = new List<MetricBudgetTagResult>(account.Tags.Length + rule.OrderedTagBudgets.Length);
        HashSet<string> configuredKeys = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < account.Tags.Length; i++)
        {
            TagValueSnapshot tag = account.Tags[i];
            string? key = TagIdentity.DecodeKeyField(tag.KeyField);
            int? configuredLimit = key is not null && rule.TryGetTagBudget(key, out int limit) ? limit : null;

            if (key is not null)
            {
                configuredKeys.Add(key);
            }

            results.Add(new MetricBudgetTagResult(
                key,
                isConfigured: configuredLimit.HasValue,
                wasObserved: true,
                configuredLimit,
                tag.ObservedDistinctValueCount,
                tag.ValueTrackingIncomplete,
                tag.UntrackedValueObservations,
                account.SeriesTrackingIncomplete,
                account.TagSetTrackingIncomplete,
                account.TagKeyCapExhausted,
                account.InstrumentTrackingIncomplete));
        }

        for (int i = 0; i < rule.OrderedTagBudgets.Length; i++)
        {
            TagBudgetLimit declared = rule.OrderedTagBudgets[i];
            if (configuredKeys.Contains(declared.Key))
            {
                continue;
            }

            results.Add(new MetricBudgetTagResult(
                declared.Key,
                isConfigured: true,
                wasObserved: false,
                declared.MaxDistinctValues,
                observedDistinctValueCount: 0,
                valueTrackingIncomplete: false,
                untrackedValueObservations: 0,
                seriesTrackingIncomplete: account.SeriesTrackingIncomplete,
                tagSetTrackingIncomplete: account.TagSetTrackingIncomplete,
                tagKeyTrackingIncomplete: account.TagKeyCapExhausted,
                instrumentTrackingIncomplete: account.InstrumentTrackingIncomplete));
        }

        results.Sort(static (left, right) =>
            string.CompareOrdinal(left.Key ?? string.Empty, right.Key ?? string.Empty));
        return results;
    }

    /// <summary>
    /// Rebuilds a public rule description from the frozen configuration. The budget object is a copy so a report
    /// never exposes a mutable configuration object that a later session change could alter.
    /// </summary>
    private static MetricBudgetBudget OriginalBudget(FrozenRule rule)
    {
        MetricBudgetTagBudget[] tags = new MetricBudgetTagBudget[rule.OrderedTagBudgets.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            TagBudgetLimit tag = rule.OrderedTagBudgets[i];
            tags[i] = new MetricBudgetTagBudget(tag.Key, tag.MaxDistinctValues);
        }

        return MetricBudgetBudget.Create(rule.MaxObservedSeries, tags);
    }

    private static string DescribeTagKey(string? key)
    {
        return key is null ? "<null tag key>" : "\"" + key + "\"";
    }
}
