using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;

namespace Alife.Function.WebBridge;

public sealed class AlifeManagementApiHost(
    AlifeManagementApiService service,
    AlifeManagementApiOptions options) : IAsyncDisposable
{
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (options.Enabled == false)
            throw new InvalidOperationException("Alife management API is disabled.");

        if (app != null)
            return;

        string? bearerToken = ResolveBearerToken();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(options.BindUrl);

        WebApplication webApplication = builder.Build();
        if (options.RequireBearerToken)
        {
            webApplication.Use(async (context, next) =>
            {
                if (IsAuthorized(context, bearerToken) == false)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next();
            });
        }

        webApplication.MapGet("/api/alife/health", () => service.GetHealth());
        webApplication.MapGet("/api/alife/status", () => service.GetStatus());
        webApplication.MapGet("/api/alife/qchat/status", () => service.GetQChatStatus());
        webApplication.MapGet(
            "/api/alife/vision/status",
            () => service.GetVisionStatus(IsAgnesVisionApiKeyConfigured()));
        webApplication.MapGet("/api/alife/tts/status", () => service.GetTtsStatus());

        await webApplication.StartAsync(cancellationToken);
        app = webApplication;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (app == null)
            return;

        WebApplication current = app;
        app = null;
        await current.StopAsync(cancellationToken);
        await current.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    string? ResolveBearerToken()
    {
        if (options.RequireBearerToken == false)
            return null;

        string? token = Environment.GetEnvironmentVariable(options.BearerTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"Required Alife management API token environment variable is not set: {options.BearerTokenEnvironmentVariable}");

        return token;
    }

    static bool IsAuthorized(HttpContext context, string? bearerToken)
    {
        if (string.IsNullOrEmpty(bearerToken))
            return true;

        string expected = $"Bearer {bearerToken}";
        string actual = context.Request.Headers.Authorization.ToString();
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    static bool IsAgnesVisionApiKeyConfigured()
    {
        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALIFE_AGNES_VISION_API_KEY")) == false;
    }

    WebApplication? app;
}
