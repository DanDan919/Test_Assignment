using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Timeouts;
using TestTask.Api.Models;
using TestTask.Api.Validation;

namespace TestTask.Api.Security;

public sealed class ApiSecurity
{
    private readonly bool development;
    private readonly byte[]? keyHash;
    private readonly HashSet<string> allowedHosts;

    public ApiSecurity(IConfiguration configuration, IHostEnvironment environment)
    {
        development = environment.IsDevelopment();
        allowedHosts = (configuration["Security:AllowedHosts"] ?? "localhost;127.0.0.1;[::1]")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var key = ReadSecret(configuration, "Security:ApiKey");
        if (!development && (key.Length < 32 || key.Length > 256 || key.Any(char.IsWhiteSpace)))
        {
            throw new InvalidOperationException(
                "Production requires Security:ApiKey or Security:ApiKeyFile (32-256 characters, no whitespace).");
        }

        keyHash = string.IsNullOrEmpty(key) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }

    public static string ReadSecret(IConfiguration configuration, string name)
    {
        var path = configuration[name + "File"];
        return string.IsNullOrWhiteSpace(path)
            ? configuration[name] ?? string.Empty
            : File.ReadAllText(path).TrimEnd('\r', '\n');
    }

    public static void Configure(WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = ProcessingLimits.RequestBodyBytes;
            options.Limits.MaxConcurrentConnections = 100;
            options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        builder.Services.AddSingleton<ApiSecurity>();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            // Trust only an explicitly configured proxy. Never trust arbitrary forwarded headers.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            var proxy = builder.Configuration["Security:TrustedProxy"];
            if (!string.IsNullOrWhiteSpace(proxy))
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
            else
            {
                // An empty trust list means "trust everyone" to this middleware.
                // Retain a loopback entry when no external proxy is configured.
                options.KnownProxies.Add(IPAddress.Loopback);
                options.KnownProxies.Add(IPAddress.IPv6Loopback);
            }
        });
        builder.Services.AddRateLimiter(options =>
        {
            // Single, bounded partition: spoofed IPs/keys cannot create unlimited limiter state.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 4,
                        QueueLimit = 0
                    })),
                PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true
                    })));
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await WriteError(context.HttpContext, 429, ErrorCodes.RateLimitExceeded,
                    "Too many requests or concurrent requests. Try again later.", token);
            };
        });
        builder.Services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(20),
                TimeoutStatusCode = StatusCodes.Status408RequestTimeout,
                WriteTimeoutResponse = context => WriteError(context, 408, ErrorCodes.RequestTimeout,
                    "The request exceeded the processing time limit.", CancellationToken.None)
            };
        });
    }

    public static void Use(WebApplication app)
    {
        var security = app.Services.GetRequiredService<ApiSecurity>(); // fail closed at startup
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                await next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client disconnected, or the timeout middleware will write its response.
                throw;
            }
            catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
            {
                await WriteError(context, exception.StatusCode,
                    exception.StatusCode == 413 ? ErrorCodes.RequestTooLarge : ErrorCodes.ValidationError,
                    "The HTTP request is invalid or too large.", context.RequestAborted);
            }
            catch (Exception exception) when (!context.Response.HasStarted)
            {
                app.Logger.LogError(exception, "Request failed. Trace identifier: {TraceId}", context.TraceIdentifier);
                var code = exception is DbException ? ErrorCodes.DatabaseError : ErrorCodes.InternalError;
                await WriteError(context, 500, code,
                    security.development ? exception.Message : "The request could not be processed.",
                    context.RequestAborted);
            }
        });
        app.UseStatusCodePages(async context =>
        {
            var status = context.HttpContext.Response.StatusCode;
            await WriteError(context.HttpContext, status,
                status == 413 ? ErrorCodes.RequestTooLarge : ErrorCodes.InvalidRequest,
                $"HTTP request rejected ({status}).", context.HttpContext.RequestAborted);
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            // A loopback port alone does not prevent browser DNS-rebinding attacks.
            if (!security.allowedHosts.Contains(context.Request.Host.Host))
            {
                await WriteError(context, 403, ErrorCodes.Forbidden,
                    "The request host is not allowed.", context.RequestAborted);
                return;
            }

            if (!security.development && !context.Request.IsHttps)
            {
                await WriteError(context, 403, ErrorCodes.HttpsRequired, "HTTPS is required.", context.RequestAborted);
                return;
            }

            if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            {
                await WriteError(context, 403, ErrorCodes.Forbidden,
                    "Cross-site browser requests are not allowed.", context.RequestAborted);
                return;
            }

            if (security.keyHash is not null)
            {
                var header = context.Request.Headers["X-API-Key"];
                var key = header.Count == 1 ? header[0] : null;
                if (key is null || key.Length > 256 || !CryptographicOperations.FixedTimeEquals(
                        security.keyHash, SHA256.HashData(Encoding.UTF8.GetBytes(key))))
                {
                    await WriteError(context, 401, ErrorCodes.Unauthorized,
                        "A valid X-API-Key header is required.", context.RequestAborted);
                    return;
                }
            }

            if (context.Request.ContentLength > ProcessingLimits.RequestBodyBytes)
            {
                await WriteError(context, 413, ErrorCodes.RequestTooLarge,
                    "The request body must not exceed 1 MiB.", context.RequestAborted);
                return;
            }

            await next(context);
        });
        if (!security.development)
        {
            app.UseHsts();
        }
        app.UseRequestTimeouts();
    }

    private static Task WriteError(HttpContext context, int status, string code, string message,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        var body = JsonSerializer.SerializeToUtf8Bytes(ProcessResponse.Failure(code, message));
        context.Response.ContentType = "application/json; charset=utf-8";
        // A complete, bounded response lets proxies finish early rejections even
        // when the client's request body is still arriving.
        context.Response.ContentLength = body.Length;
        return context.Response.Body.WriteAsync(body, cancellationToken).AsTask();
    }
}
