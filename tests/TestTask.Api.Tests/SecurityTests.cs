using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestTask.Api.Models;
using TestTask.Api.Security;
using TestTask.Api.Services;
using TestTask.Api.Validation;
using Npgsql;

namespace TestTask.Api.Tests;

public sealed class SecurityTests
{
    private const string ApiKey = "test-only-0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("0123456789abcdef0123456789abcdef ")]
    public void Production_refuses_missing_short_or_whitespace_key(string? key)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:ApiKey"] = key });
        Assert.Throws<InvalidOperationException>(() => new ApiSecurity(builder.Configuration, builder.Environment));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Production_rejects_missing_or_wrong_key(string? key)
    {
        await using var app = await StartApp("Production");
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        if (key is not null) client.DefaultRequestHeaders.Add("X-API-Key", key);
        var response = await client.PostAsJsonAsync("/api/process", TestData.CreateRequest());
        await AssertError(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Valid_key_is_accepted_only_over_https()
    {
        await using var app = await StartApp("Production");
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        client.BaseAddress = new Uri("https://localhost");
        await AssertError(await client.PostAsJsonAsync("http://localhost/api/process", TestData.CreateRequest()),
            HttpStatusCode.Forbidden, ErrorCodes.HttpsRequired);
        var response = await client.PostAsJsonAsync("/api/process", TestData.CreateRequest());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Spoofed_forwarded_https_from_untrusted_peer_does_not_bypass_tls_requirement()
    {
        await using var app = await StartApp("Production", remoteAddress: "192.0.2.5");
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        await AssertError(await client.PostAsJsonAsync("/api/process", TestData.CreateRequest()),
            HttpStatusCode.Forbidden, ErrorCodes.HttpsRequired);
    }

    [Fact]
    public async Task Configured_proxy_can_forward_https_scheme()
    {
        await using var app = await StartApp("Production", remoteAddress: "172.30.90.2");
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/process", TestData.CreateRequest())).StatusCode);
    }

    [Fact]
    public async Task Cross_site_browser_request_is_blocked_in_development()
    {
        await using var app = await StartApp("Development");
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        await AssertError(await client.PostAsJsonAsync("/api/process", TestData.CreateRequest()),
            HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Unknown_host_is_rejected_to_prevent_dns_rebinding(string environment)
    {
        await using var app = await StartApp(environment);
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://attacker.example");
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        await AssertError(await client.PostAsJsonAsync("/api/process", TestData.CreateRequest()),
            HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Database_exception_details_are_not_returned_in_production()
    {
        await using var app = await StartApp("Production", handler: _ => throw new NpgsqlException("private-db-detail"));
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        var body = await AssertError(await client.PostAsJsonAsync("/api/process", TestData.CreateRequest()),
            HttpStatusCode.InternalServerError, ErrorCodes.DatabaseError);
        Assert.DoesNotContain("private-db-detail", body.ErrorMessage);
    }

    [Fact]
    public async Task Request_timeout_cancels_async_processing_and_returns_contract()
    {
        await using var app = await StartApp("Development", handler: async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new ProcessResponse();
        }, shortTimeout: true);
        using var client = app.GetTestClient();
        await AssertError(await client.PostAsJsonAsync("/api/process", TestData.CreateRequest()),
            HttpStatusCode.RequestTimeout, ErrorCodes.RequestTimeout);
    }

    [Fact]
    public async Task Oversized_http_body_is_rejected_before_processing()
    {
        await using var app = await StartApp("Development");
        using var client = app.GetTestClient();
        await AssertError(await client.PostAsync("/api/process",
                new StringContent(new string('x', ProcessingLimits.RequestBodyBytes + 1), Encoding.UTF8, "application/json")),
            HttpStatusCode.RequestEntityTooLarge, ErrorCodes.RequestTooLarge);
    }

    [Fact]
    public async Task Global_rate_limit_cannot_be_bypassed_with_rotating_forwarded_addresses()
    {
        await using var app = await StartApp("Development");
        using var client = app.GetTestClient();
        for (var index = 0; index < 60; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/process")
            {
                Content = JsonContent.Create(TestData.CreateRequest())
            };
            request.Headers.Add("X-Forwarded-For", $"192.0.2.{index}");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
        var rejected = await client.PostAsJsonAsync("/api/process", TestData.CreateRequest());
        await AssertError(rejected, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimitExceeded);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    [Fact]
    public async Task Concurrency_limit_rejects_fifth_request_without_queuing()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        await using var app = await StartApp("Development", handler: async token =>
        {
            if (Interlocked.Increment(ref count) == 4) arrived.TrySetResult();
            await release.Task.WaitAsync(token);
            return new ProcessResponse();
        });
        using var client = app.GetTestClient();
        var pending = Enumerable.Range(0, 4)
            .Select(_ => client.PostAsJsonAsync("/api/process", TestData.CreateRequest())).ToArray();
        try
        {
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await AssertError(await client.PostAsJsonAsync("/api/process", TestData.CreateRequest()),
                HttpStatusCode.TooManyRequests, ErrorCodes.RateLimitExceeded);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(pending);
        }
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public async Task Unexpected_exception_details_are_visible_only_in_local_test_mode(string environment, bool details)
    {
        await using var app = await StartApp(environment, handler: _ => throw new InvalidOperationException("private-detail"));
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        if (environment == "Production") client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        var response = await client.PostAsJsonAsync("/api/process", TestData.CreateRequest());
        var body = await AssertError(response, HttpStatusCode.InternalServerError, ErrorCodes.InternalError);
        Assert.Equal(details, body.ErrorMessage.Contains("private-detail"));
        Assert.DoesNotContain("StackTrace", body.ErrorMessage);
    }

    [Theory]
    [InlineData(nameof(ProcessRequest.Selector), ProcessingLimits.SelectorCharacters)]
    [InlineData(nameof(ProcessRequest.Attribute), ProcessingLimits.AttributeCharacters)]
    [InlineData(nameof(ProcessRequest.UrlB64), ProcessingLimits.UrlBase64Characters)]
    [InlineData(nameof(ProcessRequest.PageB64), ProcessingLimits.PageBase64Characters)]
    [InlineData(nameof(ProcessRequest.KeyBytesB64), ProcessingLimits.KeyBase64Characters)]
    [InlineData(nameof(ProcessRequest.EncryptedTextBytesB64), ProcessingLimits.CiphertextBase64Characters)]
    public async Task Oversized_input_is_rejected_before_decoding_in_validator_and_service(string field, int limit)
    {
        var request = TestData.CreateRequest();
        typeof(ProcessRequest).GetProperty(field)!.SetValue(request, new string('A', limit + 1));
        Assert.Contains(new ProcessRequestValidator().Validate(request).Errors,
            error => error.ErrorCode == ErrorCodes.LimitExceeded);
        var store = new FakeElementStore();
        var result = await new HtmlProcessingService(store).ProcessAsync(request, CancellationToken.None);
        Assert.Equal(ErrorCodes.LimitExceeded, result.ErrorCode);
        Assert.Empty(store.SavedElements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Excessive_elements_or_emails_are_not_saved(bool emails)
    {
        var page = string.Concat(Enumerable.Repeat(emails ? "<p>a@example.com</p>" : "<a href='x'>x</a>", 1001));
        var store = new FakeElementStore();
        var result = await new HtmlProcessingService(store).ProcessAsync(
            TestData.CreateRequest(page, "a", "href"), CancellationToken.None);
        Assert.Equal(ErrorCodes.LimitExceeded, result.ErrorCode);
        Assert.Empty(store.SavedElements);
    }

    [Fact]
    public async Task Repeated_outer_html_is_bounded_before_database_write()
    {
        var page = string.Concat(Enumerable.Repeat("<div>", 20)) + new string('x', 230000) +
            string.Concat(Enumerable.Repeat("</div>", 20));
        var store = new FakeElementStore();
        var result = await new HtmlProcessingService(store).ProcessAsync(
            TestData.CreateRequest(page, "div", "class"), CancellationToken.None);
        Assert.Equal(ErrorCodes.LimitExceeded, result.ErrorCode);
        Assert.Empty(store.SavedElements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Excessive_dom_depth_or_node_count_is_rejected_before_selector_and_write(bool depth)
    {
        var page = depth
            ? string.Concat(Enumerable.Repeat("<div>", 130)) + "x" +
              string.Concat(Enumerable.Repeat("</div>", 130))
            : string.Concat(Enumerable.Repeat("<b></b>", 10001));
        var store = new FakeElementStore();
        var result = await new HtmlProcessingService(store).ProcessAsync(
            TestData.CreateRequest(page, "a", "href"), CancellationToken.None);
        Assert.Equal(ErrorCodes.LimitExceeded, result.ErrorCode);
        Assert.Empty(store.SavedElements);
    }

    [Fact]
    public async Task Storage_budget_rejection_has_predictable_error_response()
    {
        var result = await new HtmlProcessingService(new FullElementStore()).ProcessAsync(
            TestData.CreateRequest("<a href='x'>x</a>"), CancellationToken.None);
        Assert.Equal(1, result.IsError);
        Assert.Equal(ErrorCodes.LimitExceeded, result.ErrorCode);
        Assert.Empty(result.ElementsAttrList);
    }

    [Fact]
    public async Task Cancelled_request_does_not_write_to_database()
    {
        var store = new FakeElementStore();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HtmlProcessingService(store).ProcessAsync(
            TestData.CreateRequest("<a href='x'>x</a>"), new CancellationToken(canceled: true)));
        Assert.Empty(store.SavedElements);
    }

    [Fact]
    public async Task Untrusted_html_scripts_and_resource_urls_are_not_executed_or_fetched()
    {
        const string html = "<script>throw new Error('executed');</script>" +
            "<img src='http://127.0.0.1:1/private'><a href='file:///etc/passwd'>a</a>";
        var result = await new HtmlProcessingService(new FakeElementStore()).ProcessAsync(
            TestData.CreateRequest(html, "img", "src", url: "http://127.0.0.1:1/metadata"), CancellationToken.None);
        Assert.Equal(0, result.IsError);
        Assert.Equal("http://127.0.0.1:1/private", result.ElementsAttrList.Single());
    }

    private static async Task<WebApplication> StartApp(string environment,
        string remoteAddress = "192.0.2.5", Func<CancellationToken, Task<ProcessResponse>>? handler = null,
        bool shortTimeout = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:ApiKey"] = environment == "Production" ? ApiKey : null,
            ["Security:TrustedProxy"] = "172.30.90.2"
        });
        ApiSecurity.Configure(builder);
        if (shortTimeout)
        {
            builder.Services.Configure<RequestTimeoutOptions>(options => options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromMilliseconds(50),
                TimeoutStatusCode = 408,
                WriteTimeoutResponse = options.DefaultPolicy!.WriteTimeoutResponse
            });
        }
        builder.Services.AddSingleton<IHtmlProcessingService>(new StubService(handler));
        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            return next(context);
        });
        ApiSecurity.Use(app);
        app.MapPost("/api/process", (IHtmlProcessingService service, CancellationToken token) =>
            service.ProcessAsync(TestData.CreateRequest(), token));
        await app.StartAsync();
        return app;
    }

    private static async Task<ProcessResponse> AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProcessResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result.IsError);
        Assert.Equal(code, result.ErrorCode);
        Assert.Empty(result.ElementsAttrList);
        Assert.Empty(result.EmailsList);
        return result;
    }

    private sealed class StubService(Func<CancellationToken, Task<ProcessResponse>>? handler) : IHtmlProcessingService
    {
        public Task<ProcessResponse> ProcessAsync(ProcessRequest request, CancellationToken token) =>
            handler?.Invoke(token) ?? Task.FromResult(new ProcessResponse());
    }

    private sealed class FullElementStore : TestTask.Api.Data.IElementStore
    {
        public Task SaveAsync(IReadOnlyList<TestTask.Api.Data.ElementRecord> elements, CancellationToken token) =>
            throw new ProcessingLimitException();
    }
}
