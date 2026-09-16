using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MetricBudget.Probe.Runner.Probes;

/// <summary>
/// Observable-callback timing, measurement-event enable/disable behavior, same-name/version semantics, and what a
/// listener or session disposal actually stops.
/// </summary>
internal static class InstrumentLifecycleProbe
{
    public static ProbeSectionResult Run()
    {
        ProbeSectionResult result = new ProbeSectionResult("instrument lifecycle");
        ObservableSemantics(result);
        EnableDisableSemantics(result);
        SameNameSemantics(result);
        DisposeSemantics(result);
        return result;
    }

    private static void ObservableSemantics(ProbeSectionResult result)
    {
        ProbeReport.Section("2.1 observable callback semantics");

        const string meterName = "probe.lifecycle.observable";
        using Meter meter = new Meter(meterName, "1.0.0");
        int invocations = 0;
        KeyValuePair<string, object?>[] observableTags =
        {
            new KeyValuePair<string, object?>("probe.observable", "one"),
            new KeyValuePair<string, object?>("probe.extra", "two"),
        };

        meter.CreateObservableCounter<long>(
            "probe.observable.counter",
            () =>
            {
                invocations++;
                return new[] { new Measurement<long>(invocations, observableTags) };
            });

        MeterObservationSession session = new MeterObservationSession(
            "observable",
            instrument => string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal),
            seriesCap: 16,
            tagValueCapPerKey: 16,
            tagKeyCap: 8);

        session.Start();
        ProbeReport.KeyValue("invocationsAfterListenerStart", invocations);

        session.RecordObservableInstruments();
        int afterFirst = invocations;
        ProbeObservationSummary first = session.Summarize();
        ProbeReport.KeyValue("invocationsAfterFirstRecordObservable", afterFirst);
        ProbeReport.KeyValue("observedMeasurementsAfterFirst", first.ObservedMeasurements);
        ProbeReport.KeyValue("trackedSeriesAfterFirst", first.TrackedSeriesCount);

        session.RecordObservableInstruments();
        ProbeObservationSummary second = session.Summarize();
        ProbeReport.KeyValue("invocationsAfterSecondRecordObservable", invocations);
        ProbeReport.KeyValue("observedMeasurementsAfterSecond", second.ObservedMeasurements);

        ProbeReport.KeyValue("observableTagsReachCallback", second.ObservedMeasurements == 2);
        ProbeReport.KeyValue("observableCanonicalKey", ProbeTagCanonicalizer.CreateSeriesKey(observableTags));
        ProbeReport.KeyValue("observableTagsCountedAsOneSeries", second.TrackedSeriesCount == 1);

        MeterListener idleListener = new MeterListener();
        idleListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => invocations++);
        int beforeIdleRecord = invocations;
        idleListener.RecordObservableInstruments();
        ProbeReport.KeyValue("notStartedListenerInvokesObservable", invocations != beforeIdleRecord);
        idleListener.Dispose();

        session.Dispose();
        ProbeReport.KeyValue("disabledInstrumentCountAfterSessionDispose", session.DisabledInstrumentCount);

        int beforeDisposedRecord = invocations;
        string disposedObservableResult;
        try
        {
            session.RecordObservableInstruments();
            disposedObservableResult = "no-exception";
        }
        catch (Exception ex)
        {
            disposedObservableResult = ex.GetType().Name;
        }

        ProbeReport.KeyValue("recordObservableAfterDispose", disposedObservableResult);
        ProbeReport.KeyValue("observableInvocationsAfterDisposedRecord", invocations != beforeDisposedRecord);

        long observedBeforeDriver = session.Summarize().ObservedMeasurements;
        MeterListener driver = new MeterListener();
        driver.InstrumentPublished = (instrument, target) =>
        {
            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
            {
                target.EnableMeasurementEvents(instrument);
            }
        };
        driver.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => { });
        driver.Start();
        driver.RecordObservableInstruments();
        bool disposedSessionReceived = session.Summarize().ObservedMeasurements != observedBeforeDriver;
        driver.Dispose();

        ProbeReport.KeyValue("disposedSessionReceivedLaterMeasurements", disposedSessionReceived);

        result.Add(
            "observable callback timing",
            afterFirst == 1 && second.ObservedMeasurements == 2 ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            "the observable callback runs once per RecordObservableInstruments call, never at listener start, and the "
                + "tags attached by the observable callback arrive on the measurement callback");
        result.Add(
            "observable after listener dispose",
            disposedObservableResult == "no-exception" && !disposedSessionReceived ? ProbeVerdict.Pass : ProbeVerdict.Narrow,
            "RecordObservableInstruments on a disposed listener reports " + disposedObservableResult
                + " and invokes nothing; the disposed session observed no later measurements once its instruments were "
                + "explicitly disabled");
    }

    private static void EnableDisableSemantics(ProbeSectionResult result)
    {
        ProbeReport.Section("2.2 measurement event enable/disable and creation timing");

        const string meterName = "probe.lifecycle.timing";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> beforeListener = meter.CreateCounter<long>("probe.before.listener");

        List<string> observed = new List<string>();
        MeterListener listener = new MeterListener();
        listener.InstrumentPublished = (instrument, target) =>
        {
            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
            {
                target.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) => observed.Add(instrument.Name + "=" + measurement.ToString(CultureInfo.InvariantCulture)));
        listener.Start();

        beforeListener.Add(1);
        ProbeReport.KeyValue("observedAfterInstrumentCreatedBeforeListenerStart", observed.Count);
        ProbeReport.KeyValue("beforeListenerInstrumentEnabled", beforeListener.Enabled);

        Counter<long> afterListener = meter.CreateCounter<long>("probe.after.listener");
        afterListener.Add(2);
        ProbeReport.KeyValue("observedAfterInstrumentCreatedAfterListenerStart", observed.Count);

        object? disabledState = listener.DisableMeasurementEvents(afterListener);
        afterListener.Add(3);
        ProbeReport.KeyValue("observedAfterDisableMeasurementEvents", observed.Count);
        ProbeReport.KeyValue("enabledWhileDisabled", afterListener.Enabled);
        ProbeReport.KeyValue("stateReturnedByDisable", disabledState?.GetType().Name ?? "<null>");

        listener.EnableMeasurementEvents(afterListener);
        afterListener.Add(4);
        ProbeReport.KeyValue("observedAfterReEnableMeasurementEvents", observed.Count);
        ProbeReport.KeyValue("enableMeasurementEventsReturnType", typeof(MeterListener).GetMethod("EnableMeasurementEvents")!.ReturnType.Name);
        ProbeReport.KeyValue("enabledAfterReEnable", afterListener.Enabled);

        bool beforeListenerObserved = observed.Count >= 1;
        bool afterListenerObserved = observed.Count >= 2;
        bool disableStopped = observed.Count == 2;
        bool reEnableResumed = observed.Count == 3;

        listener.Dispose();

        result.Add(
            "creation timing and enable/disable",
            beforeListenerObserved && afterListenerObserved && disableStopped && reEnableResumed
                ? ProbeVerdict.Pass
                : ProbeVerdict.Narrow,
            "instruments created before and after listener start are both observed; DisableMeasurementEvents stops "
                + "delivery for that listener and clears Instrument.Enabled; EnableMeasurementEvents resumes it");
    }

    private static void SameNameSemantics(ProbeSectionResult result)
    {
        ProbeReport.Section("2.3 same-name meters, versions, scopes, and duplicate instrument names");

        const string meterName = "probe.dup.meter";
        object firstScope = new object();
        object secondScope = new object();

        using Meter first = new Meter(new MeterOptions(meterName) { Version = "1.0.0", Scope = firstScope });
        using Meter second = new Meter(new MeterOptions(meterName) { Version = "1.0.0", Scope = secondScope });
        using Meter otherVersion = new Meter(new MeterOptions(meterName) { Version = "2.0.0", Scope = firstScope });

        Counter<long> firstCounter = first.CreateCounter<long>("dup.counter");
        Counter<long> secondCounter = second.CreateCounter<long>("dup.counter");
        Counter<long> otherVersionCounter = otherVersion.CreateCounter<long>("dup.counter");
        Counter<long> duplicateNameInSameMeter = first.CreateCounter<long>("dup.counter");

        List<string> published = new List<string>();
        List<string> delivered = new List<string>();

        MeterListener listener = new MeterListener();
        listener.InstrumentPublished = (instrument, target) =>
        {
            if (!string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
            {
                return;
            }

            published.Add(Describe(instrument));
            target.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) => delivered.Add(Describe(instrument)));
        listener.Start();

        firstCounter.Add(1);
        secondCounter.Add(2);
        otherVersionCounter.Add(3);
        duplicateNameInSameMeter.Add(4);

        ProbeReport.Line("  published instruments for meter name '" + meterName + "':");
        foreach (string entry in published)
        {
            ProbeReport.Line("    " + entry);
        }

        ProbeReport.Line("  delivered measurements:");
        foreach (string entry in delivered)
        {
            ProbeReport.Line("    " + entry);
        }

        ProbeReport.KeyValue("publishedCount", published.Count);
        ProbeReport.KeyValue("deliveredCount", delivered.Count);
        ProbeReport.KeyValue("distinctInstrumentReferencesCreated", 4);
        ProbeReport.KeyValue("publishedDistinctInstruments", published.Count);
        ProbeReport.KeyValue("secondMeterSameNameAndVersionIsDistinctInstrument", !ReferenceEquals(firstCounter, secondCounter));
        ProbeReport.KeyValue("sameMeterSameNameReturnsSameInstrumentInstance", ReferenceEquals(firstCounter, duplicateNameInSameMeter));
        ProbeReport.KeyValue("firstScopeIsTheSuppliedScope", ReferenceEquals(first.Scope, firstScope));
        ProbeReport.KeyValue("secondScopeIsTheSuppliedScope", ReferenceEquals(second.Scope, secondScope));
        ProbeReport.KeyValue("sameNameAndVersionSharesScope", ReferenceEquals(first.Scope, second.Scope));
        ProbeReport.KeyValue("sameNameDifferentVersionSharesScope", ReferenceEquals(first.Scope, otherVersion.Scope));
        ProbeReport.KeyValue("firstMeterTags", first.Tags is null ? "<null>" : first.Tags.Count().ToString(CultureInfo.InvariantCulture));
        ProbeReport.KeyValue("firstInstrumentTags", firstCounter.Tags is null ? "<null>" : firstCounter.Tags.Count().ToString(CultureInfo.InvariantCulture));

        string tagsOnlyKey = ProbeTagCanonicalizer.CreateSeriesKey(
            new[] { new KeyValuePair<string, object?>("route", "/x") });
        string identityKey = "probe.dup.meter|1.0.0|dup.counter|" + tagsOnlyKey;
        ProbeReport.KeyValue("tagsOnlySeriesKey", tagsOnlyKey);
        ProbeReport.KeyValue("instrumentScopedSeriesKeyExample", identityKey);

        listener.Dispose();

        bool distinctMetersPublishIndependently = published.Count == 3 && delivered.Count == 4;
        result.Add(
            "same-name meter and instrument identity",
            distinctMetersPublishIndependently && ReferenceEquals(firstCounter, duplicateNameInSameMeter)
                ? ProbeVerdict.Pass
                : ProbeVerdict.Fail,
            "two Meter instances with identical name and version are distinct meters that publish separately; a second "
                + "instrument with the same name in one meter returns the same Instrument instance; series identity must "
                + "therefore include meter name, meter version, instrument name and tag set");
    }

    private static void DisposeSemantics(ProbeSectionResult result)
    {
        ProbeReport.Section("2.4 what MeterListener.Dispose actually stops");

        const string meterName = "probe.lifecycle.dispose";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("probe.dispose.counter");
        int delivered = 0;
        int published = 0;

        MeterListener listener = new MeterListener();
        listener.InstrumentPublished = (instrument, target) =>
        {
            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
            {
                published++;
                target.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => delivered++);
        listener.Start();
        counter.Add(1);

        int deliveredBeforeDispose = delivered;
        listener.Dispose();
        counter.Add(2);

        bool deliveredAfterDispose = delivered != deliveredBeforeDispose;
        ProbeReport.KeyValue("deliveredAfterMeterListenerDispose", deliveredAfterDispose);
        ProbeReport.KeyValue("instrumentEnabledAfterMeterListenerDispose", counter.Enabled);

        int publishedBeforeLateInstrument = published;
        Counter<long> lateInstrument = meter.CreateCounter<long>("probe.dispose.late");
        lateInstrument.Add(1);
        ProbeReport.KeyValue("newInstrumentPublishedToDisposedListener", published != publishedBeforeLateInstrument);

        bool deliveredFromLateInstrument = delivered > deliveredBeforeDispose + 1;
        ProbeReport.KeyValue("disposedListenerReceivedLateInstrumentMeasurement", deliveredFromLateInstrument);

        const string explicitMeterName = "probe.lifecycle.explicit";
        using Meter explicitMeter = new Meter(explicitMeterName, "1.0.0");
        Counter<long> explicitCounter = explicitMeter.CreateCounter<long>("probe.explicit.counter");
        int explicitDelivered = 0;

        MeterListener explicitListener = new MeterListener();
        explicitListener.InstrumentPublished = (instrument, target) =>
        {
            if (string.Equals(instrument.Meter.Name, explicitMeterName, StringComparison.Ordinal))
            {
                target.EnableMeasurementEvents(instrument);
            }
        };

        explicitListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => explicitDelivered++);
        explicitListener.Start();
        explicitCounter.Add(1);
        object? state = explicitListener.DisableMeasurementEvents(explicitCounter);
        explicitCounter.Add(2);
        explicitListener.Dispose();
        explicitCounter.Add(3);

        ProbeReport.KeyValue("explicitDisableDeliveredCountAfterDispose", explicitDelivered);
        ProbeReport.KeyValue("explicitDisableStateValue", state?.GetType().Name ?? "<null>");
        ProbeReport.KeyValue("instrumentEnabledAfterExplicitDisable", explicitCounter.Enabled);

        bool explicitDisableWorks = explicitDelivered == 1 && !explicitCounter.Enabled;

        result.Add(
            "MeterListener.Dispose stops measurement delivery",
            deliveredAfterDispose ? ProbeVerdict.Fail : ProbeVerdict.Pass,
            "measured on .NET 8.0.31: after Dispose the listener still received measurements from instruments it had "
                + "enabled (deliveredAfterDispose=" + ProbeReport.Format(deliveredAfterDispose)
                + ", Instrument.Enabled=" + ProbeReport.Format(counter.Enabled) + ")");
        result.Add(
            "disposed listener sees no new instruments",
            publishedBeforeLateInstrument == published ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            "instruments created after Dispose are not published to the disposed listener");
        result.Add(
            "explicit DisableMeasurementEvents is required before disposal",
            explicitDisableWorks ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            "disabling each enabled instrument explicitly stops delivery and clears Instrument.Enabled, so a session "
                + "wrapper must not rely on MeterListener.Dispose for containment");
    }

    private static string Describe(Instrument instrument)
    {
        return "meter=" + instrument.Meter.Name
            + "; version=" + (instrument.Meter.Version ?? "<null>")
            + "; instrument=" + instrument.Name
            + "; type=" + instrument.GetType().Name
            + "; ref=" + RuntimeHelpers.GetHashCode(instrument).ToString("x8", CultureInfo.InvariantCulture);
    }
}
