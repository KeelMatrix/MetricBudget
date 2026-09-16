// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// The kind of a <c>System.Diagnostics.Metrics</c> instrument observed by a session.
/// </summary>
/// <remarks>
/// Two instruments with the same name in one meter are distinguished by kind, and the kind is part of an
/// observed series identity so a counter and a histogram with the same name can never share accounting.
/// </remarks>
public enum MetricInstrumentKind
{
    /// <summary>The instrument kind could not be determined, which should not happen for a BCL instrument.</summary>
    Unknown = 0,

    /// <summary>A synchronous counter, for example created with <c>Meter.CreateCounter</c>.</summary>
    Counter = 1,

    /// <summary>A synchronous up/down counter, for example created with <c>Meter.CreateUpDownCounter</c>.</summary>
    UpDownCounter = 2,

    /// <summary>A synchronous histogram, for example created with <c>Meter.CreateHistogram</c>.</summary>
    Histogram = 3,

    /// <summary>An observable counter, for example created with <c>Meter.CreateObservableCounter</c>.</summary>
    ObservableCounter = 4,

    /// <summary>An observable up/down counter, for example created with <c>Meter.CreateObservableUpDownCounter</c>.</summary>
    ObservableUpDownCounter = 5,

    /// <summary>An observable gauge, for example created with <c>Meter.CreateObservableGauge</c>.</summary>
    ObservableGauge = 6,
}
