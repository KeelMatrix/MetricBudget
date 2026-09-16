using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MetricBudget.Probe.NetStandard;

namespace MetricBudget.Probe.Net472Host
{
    /// <summary>
    /// Downlevel host for the netstandard2.0 probe implementation.
    /// </summary>
    /// <remarks>
    /// This project closes one specific gate: the netstandard2.0-compiled session must actually execute on a
    /// runtime that is not .NET 8, against the netstandard2.0 asset of System.Diagnostics.DiagnosticSource that a
    /// netstandard2.0 consumer would restore. It is a probe, not product code, and it stays non-packable.
    /// </remarks>
    internal static class Program
    {
        private static int _gateFailures;

        private static int Main()
        {
            try
            {
                Console.WriteLine("MetricBudget netstandard2.0 downlevel host probe (net472)");
                HostEnvironment();
                InstrumentDelivery();
                TagIdentityAndAccounting();
                ObservableCallbacks();
                ExplicitDisable();

                Console.WriteLine();
                Console.WriteLine("=== 7.5 host verdict ===");
                Console.WriteLine("  gateFailures = " + _gateFailures.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("  net472HostVerdict = " + (_gateFailures == 0 ? "PASS" : "FAIL"));
                Console.WriteLine("  net472HostExitCode = " + (_gateFailures == 0 ? "0" : "1"));
                Console.WriteLine("net472HostRunComplete = " + (_gateFailures == 0 ? "true" : "false"));
                return _gateFailures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("net472HostRunComplete = false");
                Console.WriteLine("net472HostAborted = true");
                Console.WriteLine("  abortReason = " + ex.GetType().FullName + ": " + ex.Message);
                return 2;
            }
        }

        private static void HostEnvironment()
        {
            Console.WriteLine();
            Console.WriteLine("=== 7.0 host environment ===");

            Assembly metrics = typeof(MeterListener).Assembly;
            Assembly session = typeof(NetStandardObservationSession).Assembly;
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string? assemblyDirectory = metrics.Location.Length == 0 ? null : Path.GetDirectoryName(metrics.Location);

            Console.WriteLine("  utc = " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            Console.WriteLine("  hostRuntime = " + RuntimeInformation.FrameworkDescription);
            Console.WriteLine("  clrVersion = " + Environment.Version.ToString());
            Console.WriteLine("  os = " + RuntimeInformation.OSDescription);
            Console.WriteLine("  processArchitecture = " + RuntimeInformation.ProcessArchitecture.ToString());
            Console.WriteLine("  baseDirectory = " + baseDirectory);
            Console.WriteLine("  diagnosticSourceAssembly = " + metrics.GetName().Name + " " + metrics.GetName().Version);
            Console.WriteLine("  diagnosticSourceLocation = " + metrics.Location);
            Console.WriteLine("  diagnosticSourceLoadedFromGac = " + metrics.GlobalAssemblyCache.ToString());
            Console.WriteLine("  diagnosticSourceTargetFramework = " + TargetFramework(metrics));
            Console.WriteLine("  sessionAssembly = " + session.GetName().Name + " " + session.GetName().Version);
            Console.WriteLine("  sessionLocation = " + session.Location);
            Console.WriteLine("  sessionTargetFramework = " + TargetFramework(session));

            // The gate this host exists to close: the assembly that actually ran must be the app-local
            // netstandard2.0 asset of the package, not the net462 asset a net472 consumer would normally get and
            // not a framework or GAC copy.
            string diagnosticTargetFramework = TargetFramework(metrics);
            bool assetIsAppLocal = assemblyDirectory is not null
                && string.Equals(assemblyDirectory, baseDirectory, StringComparison.OrdinalIgnoreCase);
            bool assetIsNetStandard2 = string.Equals(
                diagnosticTargetFramework,
                ".NETStandard,Version=v2.0",
                StringComparison.Ordinal);

            Check(
                "the netstandard2.0 package asset of System.Diagnostics.DiagnosticSource is the assembly that ran",
                assetIsAppLocal && !metrics.GlobalAssemblyCache && assetIsNetStandard2,
                "loaded " + metrics.GetName().Name + " " + metrics.GetName().Version + " from " + metrics.Location
                    + " (global assembly cache: " + metrics.GlobalAssemblyCache.ToString()
                    + "; base directory: " + baseDirectory + "; asset target framework: " + diagnosticTargetFramework
                    + ", which must be .NETStandard,Version=v2.0)");
        }

        private static void InstrumentDelivery()
        {
            Console.WriteLine();
            Console.WriteLine("=== 7.1 instrument delivery on the netstandard2.0 asset ===");

            const string meterName = "probe.net472.delivery";
            using Meter meter = new Meter(meterName, "1.0.0");
            Counter<long> counter = meter.CreateCounter<long>("probe.net472.counter");
            Histogram<double> histogram = meter.CreateHistogram<double>("probe.net472.histogram");
            UpDownCounter<long> upDownCounter = meter.CreateUpDownCounter<long>("probe.net472.updowncounter");
            meter.CreateObservableCounter<long>("probe.net472.observable", () => 42L);

            using NetStandardObservationSession session = new NetStandardObservationSession(
                seriesCap: 256,
                tagValueCapPerKey: 64,
                tagKeyCap: 8);

            session.Start();
            Console.WriteLine("  publishedInstruments = " + string.Join(" | ", session.PublishedInstruments));
            Console.WriteLine("  counterEnabledAfterStart = " + NetStandardObservationSession.IsEnabled(counter).ToString());

            long counterDelivered = Delivered(session, () => NetStandardObservationSession.AddWithTagList(counter, 1L, "route", "/counter"));
            long histogramDelivered = Delivered(session, () => histogram.Record(1.5, Tagged("route", "/histogram")));
            long upDownDelivered = Delivered(session, () => upDownCounter.Add(-1L, Tagged("route", "/updowncounter")));
            long observableDelivered = Delivered(session, session.RecordObservableInstruments);

            Console.WriteLine("  counterDelivered = " + counterDelivered.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  histogramDelivered = " + histogramDelivered.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  upDownCounterDelivered = " + upDownDelivered.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  observableCounterDelivered = " + observableDelivered.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine(
                "  callbackCounts = "
                + string.Join(
                    ", ",
                    session.CallbackCounts
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture))));
            Console.WriteLine("  session = " + session.Describe());

            Check(
                "counter, histogram, upDownCounter and observableCounter all deliver",
                counterDelivered == 1 && histogramDelivered == 1 && upDownDelivered == 1 && observableDelivered == 1,
                "delivered counter=" + counterDelivered.ToString(CultureInfo.InvariantCulture)
                    + ", histogram=" + histogramDelivered.ToString(CultureInfo.InvariantCulture)
                    + ", upDownCounter=" + upDownDelivered.ToString(CultureInfo.InvariantCulture)
                    + ", observableCounter=" + observableDelivered.ToString(CultureInfo.InvariantCulture));

            Check(
                "the netstandard2.0 session tracks one series per delivered measurement",
                session.ObservedMeasurements == 4 && session.TrackedSeriesCount == 4,
                "observed=" + session.ObservedMeasurements.ToString(CultureInfo.InvariantCulture)
                    + "; trackedSeries=" + session.TrackedSeriesCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void TagIdentityAndAccounting()
        {
            Console.WriteLine();
            Console.WriteLine("=== 7.2 order-independent series identity on the netstandard2.0 asset ===");

            using Meter meter = new Meter("probe.net472.tags", "1.0.0");
            Counter<long> counter = meter.CreateCounter<long>("probe.net472.tags.counter");
            using NetStandardObservationSession session = new NetStandardObservationSession(
                seriesCap: 64,
                tagValueCapPerKey: 64,
                tagKeyCap: 8);

            session.Start();

            KeyValuePair<string, object?>[][] orders =
            {
                new[]
                {
                    new KeyValuePair<string, object?>("route", "/api/items/7"),
                    new KeyValuePair<string, object?>("tenant", "t-42"),
                },
                new[]
                {
                    new KeyValuePair<string, object?>("tenant", "t-42"),
                    new KeyValuePair<string, object?>("route", "/api/items/7"),
                },
                new[]
                {
                    new KeyValuePair<string, object?>("route", "/api/items/7"),
                    new KeyValuePair<string, object?>("tenant", "t-42"),
                },
            };

            foreach (KeyValuePair<string, object?>[] tags in orders)
            {
                counter.Add(1L, tags);
            }

            Console.WriteLine("  delivered = " + session.ObservedMeasurements.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  trackedSeries = " + session.TrackedSeriesCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  session = " + session.Describe());

            Check(
                "three deliveries of the same tag set in different orders track one series",
                session.ObservedMeasurements == 3 && session.TrackedSeriesCount == 1,
                "observed=" + session.ObservedMeasurements.ToString(CultureInfo.InvariantCulture)
                    + "; trackedSeries=" + session.TrackedSeriesCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void ObservableCallbacks()
        {
            Console.WriteLine();
            Console.WriteLine("=== 7.3 observable callback behavior on the netstandard2.0 asset ===");

            using Meter meter = new Meter("probe.net472.observable", "1.0.0");
            int invocations = 0;
            meter.CreateObservableCounter<long>(
                "probe.net472.observable.counter",
                () =>
                {
                    invocations++;
                    return 5L;
                });

            using NetStandardObservationSession session = new NetStandardObservationSession(
                seriesCap: 64,
                tagValueCapPerKey: 64,
                tagKeyCap: 8);

            session.Start();
            int afterStart = invocations;
            session.RecordObservableInstruments();
            int afterFirst = invocations;
            long observedAfterFirst = session.ObservedMeasurements;
            session.RecordObservableInstruments();
            int afterSecond = invocations;

            Console.WriteLine("  invocationsAfterListenerStart = " + afterStart.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  invocationsAfterFirstRecord = " + afterFirst.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  invocationsAfterSecondRecord = " + afterSecond.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  observedAfterFirstRecord = " + observedAfterFirst.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  observedAfterSecondRecord = " + session.ObservedMeasurements.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  trackedSeriesAfterSecondRecord = " + session.TrackedSeriesCount.ToString(CultureInfo.InvariantCulture));

            Check(
                "observable callbacks run once per RecordObservableInstruments call and re-use one series",
                afterStart == 0 && afterFirst == 1 && afterSecond == 2
                    && observedAfterFirst == 1 && session.ObservedMeasurements == 2
                    && session.TrackedSeriesCount == 1,
                "invocations start=" + afterStart.ToString(CultureInfo.InvariantCulture)
                    + ", first=" + afterFirst.ToString(CultureInfo.InvariantCulture)
                    + ", second=" + afterSecond.ToString(CultureInfo.InvariantCulture)
                    + "; observed=" + session.ObservedMeasurements.ToString(CultureInfo.InvariantCulture)
                    + "; trackedSeries=" + session.TrackedSeriesCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void ExplicitDisable()
        {
            Console.WriteLine();
            Console.WriteLine("=== 7.4 explicit disable and disposal containment on the downlevel host ===");

            const string disableMeterName = "probe.net472.disable";
            using (Meter meter = new Meter(disableMeterName, "1.0.0"))
            {
                Counter<long> counter = meter.CreateCounter<long>("probe.net472.disable.counter");
                int delivered = 0;

                MeterListener listener = new MeterListener();
                listener.InstrumentPublished = (instrument, target) =>
                {
                    if (string.Equals(instrument.Meter.Name, disableMeterName, StringComparison.Ordinal))
                    {
                        target.EnableMeasurementEvents(instrument);
                    }
                };

                listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => delivered++);
                listener.Start();

                counter.Add(1L);
                int deliveredBeforeDisable = delivered;
                object? state = listener.DisableMeasurementEvents(counter);
                bool enabledAfterDisable = counter.Enabled;
                counter.Add(2L);
                int deliveredAfterDisable = delivered;
                listener.Dispose();

                Console.WriteLine("  deliveredBeforeDisable = " + deliveredBeforeDisable.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("  deliveredAfterDisable = " + deliveredAfterDisable.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("  instrumentEnabledAfterDisable = " + enabledAfterDisable.ToString());
                Console.WriteLine("  disableStateValue = " + (state is null ? "<null>" : state.GetType().Name));
                Console.WriteLine("  deliveredAfterDisableThenDispose = " + delivered.ToString(CultureInfo.InvariantCulture));

                Check(
                    "explicit DisableMeasurementEvents stops delivery on the downlevel host",
                    deliveredAfterDisable == deliveredBeforeDisable && !enabledAfterDisable,
                    "delivered was " + deliveredBeforeDisable.ToString(CultureInfo.InvariantCulture)
                        + " before and " + deliveredAfterDisable.ToString(CultureInfo.InvariantCulture)
                        + " after DisableMeasurementEvents; Instrument.Enabled=" + enabledAfterDisable.ToString());
            }

            // Measured platform fact, not a gate. This runs last and deliberately does not clean up, because the
            // point is what Dispose alone does to an instrument the listener enabled.
            const string disposeMeterName = "probe.net472.dispose";
            using Meter disposeMeter = new Meter(disposeMeterName, "1.0.0");
            Counter<long> disposeCounter = disposeMeter.CreateCounter<long>("probe.net472.dispose.counter");
            int disposeDelivered = 0;
            MeterListener disposeListener = new MeterListener();
            disposeListener.InstrumentPublished = (instrument, target) =>
            {
                if (string.Equals(instrument.Meter.Name, disposeMeterName, StringComparison.Ordinal))
                {
                    target.EnableMeasurementEvents(instrument);
                }
            };

            disposeListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => disposeDelivered++);
            disposeListener.Start();
            disposeCounter.Add(1L);
            int deliveredBeforeDispose = disposeDelivered;
            disposeListener.Dispose();
            disposeCounter.Add(2L);
            int deliveredAfterDispose = disposeDelivered;

            Console.WriteLine("  disposeOnlyDeliveredBefore = " + deliveredBeforeDispose.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  disposeOnlyDeliveredAfterDispose = " + deliveredAfterDispose.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  disposeOnlyInstrumentEnabledAfterDispose = " + disposeCounter.Enabled.ToString());
            Console.WriteLine(
                "VERDICT[" + (deliveredAfterDispose == deliveredBeforeDispose ? "PASS" : "FAIL")
                + "] MeterListener.Dispose alone stops measurement delivery on the downlevel host - delivered was "
                + deliveredBeforeDispose.ToString(CultureInfo.InvariantCulture) + " before and "
                + deliveredAfterDispose.ToString(CultureInfo.InvariantCulture) + " after Dispose; Instrument.Enabled="
                + disposeCounter.Enabled.ToString());
        }

        private static long Delivered(NetStandardObservationSession session, Action record)
        {
            long before = session.ObservedMeasurements;
            record();
            return session.ObservedMeasurements - before;
        }

        private static TagList Tagged(string key, string value)
        {
            TagList tags = default;
            tags.Add(key, value);
            return tags;
        }

        private static string TargetFramework(Assembly assembly)
        {
            TargetFrameworkAttribute? attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            return attribute?.FrameworkName ?? "<none>";
        }

        private static void Check(string item, bool passed, string detail)
        {
            if (!passed)
            {
                _gateFailures++;
            }

            Console.WriteLine("VERDICT[" + (passed ? "PASS" : "FAIL") + "] " + item + " - " + detail);
        }
    }
}
