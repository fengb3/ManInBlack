using ManInBlack.AI.Abstraction.Attributes;
using GitHubAdaptor.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace GitHubAdaptor.Services;

[ServiceRegister.Singleton]
public class GitHubAppTokenService(
    GitHubSettings settings,
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubAppTokenService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<long, (string Token, DateTime ExpiresAt)> _tokenCache = [];

    public async Task<string> GetInstallationTokenAsync(long installationId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_tokenCache.TryGetValue(installationId, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
                return cached.Token;

            var jwt = GenerateJwt();
            var token = await ExchangeInstallationTokenAsync(jwt, installationId, ct);

            _tokenCache[installationId] = (token, DateTime.UtcNow.AddMinutes(55));

            logger.LogInformation("获取 installation token 成功，installation_id: {InstallationId}", installationId);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GenerateJwt()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = $"{{\"iat\":{now - 60},\"exp\":{now + 600},\"iss\":\"{settings.AppId}\"}}";
        var header = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";

        var headerBytes = Encoding.UTF8.GetBytes(header);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var message = $"{Base64UrlUrlEncode(headerBytes)}.{Base64UrlUrlEncode(payloadBytes)}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(settings.PrivateKeyPath));

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(message),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{message}.{Base64UrlUrlEncode(signature)}";
    }

    private async Task<string> ExchangeInstallationTokenAsync(string jwt, long installationId, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitHubAdaptor", "1.0"));

        var response = await client.PostAsync(
            $"https://api.github.com/app/installations/{installationId}/access_tokens",
            null, ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("GitHub API 未返回 token");
    }

    private static string Base64UrlUrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}