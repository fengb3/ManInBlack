using GitHubAdaptor.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Webhook;

public class GitHubWebhookMiddleware(
    RequestDelegate next,
    GitHubSettings settings,
    ILogger<GitHubWebhookMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(settings.WebhookEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var signatureHeader = context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (string.IsNullOrEmpty(signatureHeader) || !VerifySignature(body, signatureHeader))
        {
            logger.LogWarning("Webhook 签名验证失败");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid signature");
            return;
        }

        context.Items["RawBody"] = body;
        await next(context);
    }

    private bool VerifySignature(string body, string signatureHeader)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = $"sha256={Convert.ToHexStringLower(hash)}";
        return string.Equals(signatureHeader, expected, StringComparison.OrdinalIgnoreCase);
    }
}
