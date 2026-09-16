// Copyright (c) KeelMatrix

#if NET8_0_OR_GREATER

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Representative ASP.NET Core instrumentation, verified without an exporter, collector, or backend.
/// </summary>
/// <remarks>
/// ASP.NET Core publishes its hosting metrics through <c>System.Diagnostics.Metrics</c> whether or not an
/// OpenTelemetry SDK is present, so this is the same measurement surface an OpenTelemetry-flavoured application
/// produces. The test makes real loopback requests to a real Kestrel host and asserts the observed cardinality of
/// <c>http.server.request.duration</c>, including its <c>http.response.status_code</c> tag.
/// </remarks>
public sealed class AspNetCoreIntegrationTests
{
    private const string MeterName = "Microsoft.AspNetCore.Hosting";
    private const string InstrumentName = "http.server.request.duration";

    [Fact]
    public async Task ServerRequestCardinalityIsVerifiedWithinBudget()
    {
        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(MeterName, InstrumentName, budget =>
            {
                budget.MaxObservedSeries = 8;
                budget.Tag("http.response.status_code").MaxDistinctValues = 2;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        await ExerciseServerAsync();

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(1, report.ObservedInstrumentCount);

        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);
        Assert.Equal(MeterName, instrument.MeterName);
        Assert.Equal(InstrumentName, instrument.InstrumentName);
        Assert.Equal(MetricInstrumentKind.Histogram, instrument.InstrumentKind);
        Assert.True(instrument.MeasurementCount >= 2);

        MetricBudgetTagResult status = instrument.Tags.Single(tag => tag.Key == "http.response.status_code");
        Assert.Equal(2, status.ObservedDistinctValueCount);
        Assert.True(status.IsWithinBudget);
    }

    [Fact]
    public async Task ServerRequestCardinalityFailsWhenATagBudgetIsTooSmall()
    {
        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(MeterName, InstrumentName, budget =>
            {
                budget.MaxObservedSeries = 8;
                budget.Tag("http.response.status_code").MaxDistinctValues = 1;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        await ExerciseServerAsync();

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Violation, report.Outcome);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.TagDistinctValuesBudgetExceeded);
        Assert.Equal("http.response.status_code", violation.TagKey);
        Assert.Equal(2, violation.ObservedCount);
        Assert.Equal(1, violation.ConfiguredLimit);
    }

    private static async Task ExerciseServerAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        WebApplication app = builder.Build();
        app.MapGet("/", static () => "ok");

        await app.StartAsync();
        try
        {
            string address = app.Urls.First();

            using HttpClient client = new HttpClient();
            using HttpResponseMessage success = await client.GetAsync(new Uri(address + "/"));
            Assert.Equal(System.Net.HttpStatusCode.OK, success.StatusCode);

            using HttpResponseMessage missing = await client.GetAsync(new Uri(address + "/missing"));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

#endif
