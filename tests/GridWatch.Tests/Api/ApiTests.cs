using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GridWatch.Api.Hubs;
using GridWatch.Infrastructure.Ingestion;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace GridWatch.Tests.Api;

public sealed class ApiTests(GridWatchApiFactory factory) : IClassFixture<GridWatchApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_check_reports_healthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Snapshot_returns_the_latest_value_of_each_source()
    {
        var json = await GetJson("/api/grid/snapshot");

        // Last interval and hour of the sample: market day 2026-10-30, hour ending 24.
        Assert.Equal("2026-10-31T04:55:00Z", json.GetProperty("liveDemand").GetProperty("intervalStartUtc").GetString());
        Assert.Equal("2026-10-31T04:55:00Z", json.GetProperty("price").GetProperty("intervalStartUtc").GetString());
        Assert.Equal("2026-10-31T04:00:00Z", json.GetProperty("mix").GetProperty("hourStartUtc").GetString());

        var mix = json.GetProperty("mix").GetProperty("mix");
        Assert.Equal(7, mix.GetProperty("fuels").GetArrayLength());
        Assert.Equal("Nuclear", mix.GetProperty("fuels")[0].GetProperty("fuel").GetString());
        Assert.InRange(mix.GetProperty("nonEmittingShare").GetDouble(), 0, 1);
    }

    [Fact]
    public async Task Realtime_defaults_to_the_last_24_hours_of_5_minute_data()
    {
        var json = await GetJson("/api/grid/realtime");

        Assert.Equal(288, json.GetArrayLength());
        Assert.True(json[0].GetProperty("pricePerMwh").ValueKind == JsonValueKind.Number);
        Assert.True(json[0].GetProperty("ontarioDemandMw").GetDouble() > 5000);
    }

    [Fact]
    public async Task Generation_can_be_requested_as_daily_averages()
    {
        var json = await GetJson("/api/grid/generation?resolution=Day&from=2026-10-24T05:00:00Z&to=2026-10-31T05:00:00Z");

        Assert.Equal(7, json.GetArrayLength());
        Assert.True(json[0].GetProperty("outputMw").GetProperty("Nuclear").GetInt32() > 0);
    }

    [Fact]
    public async Task Daily_summary_lists_newest_day_first()
    {
        var json = await GetJson("/api/grid/daily");

        Assert.Equal("2026-10-30", json[0].GetProperty("marketDate").GetString());
        Assert.True(json[0].GetProperty("peakOntarioDemandMw").GetInt32() > 10000);
    }

    [Theory]
    [InlineData("/api/grid/demand?from=2026-10-30&to=2026-10-01")]
    [InlineData("/api/grid/demand?from=2026-01-01&to=2026-10-01")]
    [InlineData("/api/grid/realtime?from=2026-10-01&to=2026-10-30")]
    public async Task Invalid_ranges_return_problem_details(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid time range", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Unknown_API_routes_return_404()
    {
        var response = await _client.GetAsync("/api/grid/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Built_UI_serves_its_scripts_as_JavaScript()
    {
        // Only meaningful when the React build is present in wwwroot (CI's API job does not build it).
        var html = await _client.GetAsync("/");
        if (html.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var page = await html.Content.ReadAsStringAsync();
        var script = System.Text.RegularExpressions.Regex.Match(page, "src=\"(/assets/[^\"]+\\.js)\"").Groups[1].Value;
        Assert.NotEmpty(script);

        var asset = await _client.GetAsync(script);

        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Ontario Grid Watch", await (await _client.GetAsync("/some/client/route")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await _client.GetAsync("/api/grid/snapshot");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Manual_ingestion_rejects_missing_or_wrong_keys(string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ingestion/run?target=Realtime");
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Manual_ingestion_runs_with_the_admin_key_and_is_recorded()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ingestion/run?target=Demand");
        request.Headers.Add("X-Api-Key", GridWatchApiFactory.AdminKey);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        // The fake IESO has no demand file registered, so the run fails cleanly and is logged.
        Assert.Equal("Failed", results[0].GetProperty("status").GetString());

        var runs = await GetJson("/api/ingestion/runs");
        Assert.Contains(runs.EnumerateArray(), r => r.GetProperty("source").GetString() == "Demand");
    }

    [Fact]
    public async Task Clients_receive_a_SignalR_message_when_the_grid_updates()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/grid"), o => o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On(GridHub.GridUpdated, () => received.TrySetResult());
        await hub.StartAsync();

        await factory.Services.GetRequiredService<IGridNotifier>().GridUpdatedAsync(CancellationToken.None);

        await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await hub.DisposeAsync();
    }

    private async Task<JsonElement> GetJson(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
