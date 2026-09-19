// Copyright (c) KeelMatrix

using System.Globalization;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Checks that a snapshot describes a possible accounting state.
/// </summary>
/// <remarks>
/// The session writes every counter under one lock, so these invariants hold by construction. They are verified
/// again when a report is built so that an impossible state fails loudly instead of surfacing as a passing
/// verification.
/// </remarks>
internal static class AccountingInvariants
{
    /// <summary>
    /// Verifies every invariant and appends one record per violation.
    /// </summary>
    /// <param name="snapshot">Snapshot to verify.</param>
    /// <param name="problems">Receives a description of every violated invariant.</param>
    /// <returns><see langword="true"/> when the snapshot is consistent.</returns>
    internal static bool Validate(SessionSnapshot snapshot, List<string> problems)
    {
        int before = problems.Count;

        if (snapshot.AccountedMeasurements + snapshot.UnmatchedMeasurements != snapshot.MeasurementsDelivered)
        {
            problems.Add(
                "accounting is inconsistent: "
                + snapshot.MeasurementsDelivered.ToString(CultureInfo.InvariantCulture)
                + " delivered measurement(s) do not equal "
                + snapshot.AccountedMeasurements.ToString(CultureInfo.InvariantCulture)
                + " accounted plus "
                + snapshot.UnmatchedMeasurements.ToString(CultureInfo.InvariantCulture)
                + " unattributed measurement(s)");
        }

        if (snapshot.UnmatchedMeasurements != 0)
        {
            problems.Add(
                "accounting is inconsistent: "
                + snapshot.UnmatchedMeasurements.ToString(CultureInfo.InvariantCulture)
                + " measurement(s) arrived from an instrument the session did not enable");
        }

        if (snapshot.UntrackedInstrumentIdentities > 0 && !snapshot.InstrumentIdentityTrackingIncomplete)
        {
            problems.Add("accounting is inconsistent: instrument identities were dropped without reporting an identity bound");
        }

        if (snapshot.UntrackedInstrumentInstances > 0 && !snapshot.InstrumentInstanceTrackingIncomplete)
        {
            problems.Add("accounting is inconsistent: physical instrument instances were dropped without reporting an instance bound");
        }

        if (snapshot.UntrackedConflicts > 0 && !snapshot.ConflictTrackingIncomplete)
        {
            problems.Add("accounting is inconsistent: configuration conflicts were dropped without reporting a conflict bound");
        }

        for (int i = 0; i < snapshot.Instruments.Length; i++)
        {
            InstrumentAccountSnapshot instrument = snapshot.Instruments[i];
            string identity = instrument.Identity.Describe();

            long classified = instrument.NewSeriesObservations
                + instrument.ExistingSeriesObservations
                + instrument.UntrackedSeriesObservations
                + instrument.UntrackedTagSetObservations;
            if (classified != instrument.MeasurementCount)
            {
                problems.Add(
                    "accounting is inconsistent for " + identity + ": "
                    + classified.ToString(CultureInfo.InvariantCulture)
                    + " classified observations do not equal "
                    + instrument.MeasurementCount.ToString(CultureInfo.InvariantCulture) + " measurement(s)");
            }

            if (instrument.ObservedSeriesCount != instrument.NewSeriesObservations)
            {
                problems.Add(
                    "accounting is inconsistent for " + identity + ": tracked series ("
                    + instrument.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture)
                    + ") do not equal new-series observations ("
                    + instrument.NewSeriesObservations.ToString(CultureInfo.InvariantCulture) + ")");
            }

            if (instrument.ObservedSeriesCount > instrument.MeasurementCount)
            {
                problems.Add(
                    "accounting is inconsistent for " + identity + ": tracked series ("
                    + instrument.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture)
                    + ") outnumber measurements ("
                    + instrument.MeasurementCount.ToString(CultureInfo.InvariantCulture) + ")");
            }

            if (instrument.ObservedSeriesCount > snapshot.MaxTrackedSeries)
            {
                problems.Add(
                    "accounting is inconsistent for " + identity + ": tracked series ("
                    + instrument.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture)
                    + ") exceed the configured series bound ("
                    + snapshot.MaxTrackedSeries.ToString(CultureInfo.InvariantCulture) + ")");
            }

            if (instrument.UntrackedSeriesObservations > 0 && !instrument.SeriesCapExhausted)
            {
                problems.Add(
                    "accounting is inconsistent for " + identity + ": untracked observations were counted without "
                    + "reporting that the series bound was reached");
            }

            for (int j = 0; j < instrument.Tags.Length; j++)
            {
                TagValueSnapshot tag = instrument.Tags[j];
                if (tag.ObservedDistinctValueCount > snapshot.MaxTrackedValuesPerTag)
                {
                    problems.Add(
                        "accounting is inconsistent for " + identity + ": tag value count ("
                        + tag.ObservedDistinctValueCount.ToString(CultureInfo.InvariantCulture)
                        + ") exceeds the configured per-tag bound ("
                        + snapshot.MaxTrackedValuesPerTag.ToString(CultureInfo.InvariantCulture) + ")");
                }

                if (tag.ObservedDistinctValueCount > instrument.MeasurementCount)
                {
                    problems.Add(
                        "accounting is inconsistent for " + identity + ": tag value count ("
                        + tag.ObservedDistinctValueCount.ToString(CultureInfo.InvariantCulture)
                        + ") exceeds the measurement count ("
                        + instrument.MeasurementCount.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
        }

        return problems.Count == before;
    }
}
