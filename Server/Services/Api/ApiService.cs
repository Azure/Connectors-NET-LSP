using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

using Azure.Core;
using Azure.Identity;

using ZiggyCreatures.Caching.Fusion;

namespace SdkLspServer.Services.Api;

/// <summary>
/// Service for making dynamic API calls based on user input.
/// This can be used to fetch real-time data, SDK metadata, or other external resources
/// to enhance LSP features like completions, hover info, etc.
/// </summary>
/// <remarks>
/// Authentication: If <see cref="ApiServiceConfig.BearerToken"/> is explicitly set (e.g., via the
/// <c>connectorSdk.azure.bearerToken</c> VS Code setting), that token is used directly. Otherwise,
/// <see cref="DefaultAzureCredential"/> acquires a token for the API Hub scope automatically —
/// the same credential chain the Connector SDK clients use at runtime. This means <c>az login</c>
/// is sufficient for local development; no manual bearer token configuration is needed.
/// </remarks>
public class ApiService
{
    /// <summary>
    /// The OAuth scope required by Azure API Hub connections. This is the same scope used by the
    /// generated SDK clients (e.g., <c>Office365Client</c>, <c>SharepointonlineClient</c>).
    /// </summary>
    private static readonly string[] ApiHubScopes = ["https://apihub.azure.com/.default"];

    public ApiServiceConfig Config;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly IFusionCache cache;
    private readonly TokenCredential credential;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private volatile CachedToken? cachedToken;

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresOn);

    public ApiService(IHttpClientFactory httpClientFactory, ApiServiceConfig config, IFusionCache fusionCache)
    {
        this.httpClientFactory = httpClientFactory;
        Config = config;
        cache = fusionCache;
        credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            // NOTE(daviburg): Only keep AzureCliCredential for local dev. All other providers
            // are excluded to avoid wasted time, unexpected UI prompts, or auth failures.
            ExcludeEnvironmentCredential = true,
            ExcludeWorkloadIdentityCredential = true,
            ExcludeManagedIdentityCredential = true,
            ExcludeInteractiveBrowserCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzurePowerShellCredential = true,
            ExcludeAzureDeveloperCliCredential = true,
        });
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    /// <summary>
    /// Gets a bearer token for API Hub. Uses the explicit <see cref="ApiServiceConfig.BearerToken"/>
    /// if configured; otherwise acquires one via <see cref="DefaultAzureCredential"/>.
    /// </summary>
    /// <remarks>
    /// Token acquisition intentionally uses a dedicated 30-second timeout instead of the
    /// caller's token. This prevents hover cancellation (user moving the cursor) from killing a
    /// slow <c>az account get-access-token</c> call mid-flight, while ensuring the server recovers
    /// if the credential provider hangs. The acquired token is cached and reused by all subsequent
    /// requests.
    /// </remarks>
    private async Task<string> GetBearerTokenAsync()
    {
        // Prefer explicit token from VS Code settings (manual override).
        if (!string.IsNullOrEmpty(Config.BearerToken))
        {
            return Config.BearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? Config.BearerToken[7..].Trim()
                : Config.BearerToken;
        }

        // cachedToken is a volatile reference to an immutable record, so this read is atomic.
        CachedToken? snapshot = cachedToken;
        if (snapshot != null && snapshot.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return snapshot.Value;
        }

        // Auto-acquire via DefaultAzureCredential (same as the SDK clients).
        // Lock timeout matches token acquisition timeout (30s) so concurrent callers
        // wait long enough for a cold acquisition to complete instead of timing out early.
        try
        {
            using var lockCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await tokenLock.WaitAsync(lockCts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Token acquisition timed out after 30s waiting for lock. Another token request may be stuck.");
        }

        try
        {
            // Re-check cache after acquiring lock — another caller may have refreshed it.
            snapshot = cachedToken;
            if (snapshot != null && snapshot.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return snapshot.Value;
            }

            using var tokenCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var newToken = await credential.GetTokenAsync(
                new TokenRequestContext(ApiHubScopes), tokenCts.Token);

            cachedToken = new CachedToken(newToken.Token, newToken.ExpiresOn);

            return newToken.Token;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Token acquisition timed out after 30s. Ensure 'az login' has been run and the Azure CLI is responsive.");
        }
        finally
        {
            tokenLock.Release();
        }
    }

    /// <summary>
    /// Adds the Authorization header to the HttpClient using the resolved bearer token.
    /// </summary>
    private async Task AuthenticateClientAsync(HttpClient client)
    {
        string token = await GetBearerTokenAsync();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Makes a GET request to the specified URL and returns the response as a string.
    /// Cached for 5 minutes with fail-safe mode (serves stale data for up to 1 hour if API fails).
    /// </summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response content as a string, or null if the request fails.</returns>
    public async Task<string?> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"GET:{url}";

        return await cache.GetOrSetAsync<string?>(
            cacheKey,
            async (ctx, ct) =>
            {
                try
                {
                    HttpClient client = httpClientFactory.CreateClient("SdkLspClient");
                    using HttpResponseMessage response = await client.GetAsync(url, ct);

                    return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"[ApiService] GET error: {ex.Message}");
                    return null;
                }
            },
            options => options
                .SetDuration(TimeSpan.FromMinutes(5))
                .SetFailSafe(true, TimeSpan.FromHours(1))
                .SetFactoryTimeouts(TimeSpan.FromSeconds(10), keepTimedOutFactoryResult: false),
            cancellationToken);
    }

    /// <summary>
    /// Makes a GET request and deserializes the JSON response to the specified type.
    /// Cached for 5 minutes with fail-safe mode (serves stale data for up to 1 hour if API fails).
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="url">The URL to request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized object, or default if the request fails.</returns>
    public async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"GET_JSON:{typeof(T).FullName}:{url}";

        return await cache.GetOrSetAsync<T?>(
            cacheKey,
            async (ctx, ct) =>
            {
                try
                {
                    HttpClient client = httpClientFactory.CreateClient("SdkLspClient");

                    await AuthenticateClientAsync(client);

                    return await client.GetFromJsonAsync<T>(url, jsonOptions, ct);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"[ApiService] GET JSON error: {ex.Message}");
                    return default;
                }
            },
            options => options
                .SetDuration(TimeSpan.FromMinutes(5))
                .SetFailSafe(true, TimeSpan.FromHours(1))
                .SetFactoryTimeouts(TimeSpan.FromSeconds(30), keepTimedOutFactoryResult: false),
            cancellationToken);
    }

    /// <summary>
    /// Makes a POST request with JSON payload and returns the response as a string.
    /// Cached for 5 minutes with fail-safe mode (serves stale data for up to 1 hour if API fails).
    /// Cache key includes URL and payload hash for proper invalidation.
    /// </summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="payload">The object to serialize and send as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response content as a string, or null if the request fails.</returns>
    public async Task<string?> PostJsonAsync(string url, object payload, CancellationToken cancellationToken = default)
    {
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, jsonOptions);
        string payloadHash = ComputeHash(payloadBytes);
        string cacheKey = $"POST:{url}:{payloadHash}";

        return await cache.GetOrSetAsync<string?>(
            cacheKey,
            async (ctx, ct) =>
            {
                try
                {
                    HttpClient client = httpClientFactory.CreateClient("SdkLspClient");

                    await AuthenticateClientAsync(client);

                    using var content = new ByteArrayContent(payloadBytes);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                    using HttpResponseMessage response = await client.PostAsync(url, content, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync(ct);
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"[ApiService] POST error: {ex.Message}");
                    return null;
                }
            },
            options => options
                .SetDuration(TimeSpan.FromMinutes(5))
                .SetFailSafe(true, TimeSpan.FromHours(1))
                .SetFactoryTimeouts(TimeSpan.FromSeconds(30), keepTimedOutFactoryResult: false),
            cancellationToken);
    }

    /// <summary>
    /// Makes a POST request with JSON payload and deserializes the response to the specified type.
    /// Cached for 5 minutes with fail-safe mode (serves stale data for up to 1 hour if API fails).
    /// Cache key includes URL, payload hash, and response type for proper invalidation.
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response to.</typeparam>
    /// <param name="url">The URL to request.</param>
    /// <param name="payload">The object to serialize and send as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response object, or default if the request fails.</returns>
    public async Task<TResponse?> PostJsonAsync<TResponse>(string url, object payload, CancellationToken cancellationToken = default)
    {
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, jsonOptions);
        string payloadHash = ComputeHash(payloadBytes);
        string cacheKey = $"POST_JSON:{typeof(TResponse).FullName}:{url}:{payloadHash}";

        return await cache.GetOrSetAsync<TResponse?>(
            cacheKey,
            async (ctx, ct) =>
            {
                try
                {
                    HttpClient client = httpClientFactory.CreateClient("SdkLspClient");

                    await AuthenticateClientAsync(client);

                    using var content = new ByteArrayContent(payloadBytes);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                    using HttpResponseMessage response = await client.PostAsync(url, content, ct);
                    string responseBody = await response.Content.ReadAsStringAsync(ct);

                    await Console.Error.WriteLineAsync($"[ApiService] POST status: {(int)response.StatusCode} {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        TResponse? result = JsonSerializer.Deserialize<TResponse>(responseBody, jsonOptions);
                        return result;
                    }

                    await Console.Error.WriteLineAsync($"[ApiService] POST non-success: {(int)response.StatusCode} {response.StatusCode}");
                    return default;
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"[ApiService] POST error: {ex.Message}");
                    return default;
                }
            },
            options => options
                .SetDuration(TimeSpan.FromMinutes(5))
                .SetFailSafe(true, TimeSpan.FromHours(1))
                .SetFactoryTimeouts(TimeSpan.FromSeconds(30), keepTimedOutFactoryResult: false),
            cancellationToken);
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hash of the input bytes for use as a cache key component.
    /// </summary>
    /// <param name="input">The bytes to hash.</param>
    private static string ComputeHash(byte[] input)
    {
        byte[] hashBytes = SHA256.HashData(input);
        return Convert.ToHexString(hashBytes);
    }
}
