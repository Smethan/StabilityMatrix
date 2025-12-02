using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using StabilityMatrix.Core.Handlers;
using StabilityMatrix.Core.Models.Configs;

namespace StabilityMatrix.Core.Api;

public class ApiFactory : IApiFactory
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<ApiFactory>? logger;
    public RefitSettings? RefitSettings { get; init; }

    public ApiFactory(IHttpClientFactory httpClientFactory, ILogger<ApiFactory>? logger = null)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    public T CreateRefitClient<T>(Uri baseAddress)
    {
        var httpClient = httpClientFactory.CreateClient(nameof(T));
        httpClient.BaseAddress = baseAddress;
        return RestService.For<T>(httpClient, RefitSettings);
    }

    public T CreateRefitClient<T>(Uri baseAddress, RefitSettings refitSettings)
    {
        var httpClient = httpClientFactory.CreateClient(nameof(T));
        httpClient.BaseAddress = baseAddress;

        return RestService.For<T>(httpClient, refitSettings);
    }

    public T CreateRefitClient<T>(
        Uri baseAddress,
        RefitSettings refitSettings,
        IReadOnlyDictionary<string, string>? defaultHeaders
    )
    {
        // Always start with a factory-created client to get proper configuration
        // (timeout, retry policies, etc.)
        var factoryClient = httpClientFactory.CreateClient(nameof(T));
        factoryClient.BaseAddress = baseAddress;

        // If headers are provided, we need to wrap the factory's handler with our custom handler chain
        if (defaultHeaders != null && defaultHeaders.Count > 0)
        {
            // Create ComfyServerSettings with the headers
            var serverSettings = new ComfyServerSettings
            {
                Headers = new Dictionary<string, string>(defaultHeaders),
            };

            // Wrap in IOptions
            var options = Options.Create(serverSettings);

            // Extract the factory's handler chain using reflection
            // The factory client's handler contains retry policies and other configurations
            // We need to wrap it with our custom handlers to preserve those settings
            var factoryHandler = GetHandlerFromHttpClient(factoryClient, logger);

            // Create handler chain:
            // CloudflareAccessRedirectHandler (outermost - detects redirects)
            // -> CloudflareCookieHandler (manages CF_Authorization cookies)
            // -> AuthHeaderHandler (adds custom headers)
            // -> Factory's handler chain (preserves timeout, retry policies, etc.)
            var cookieHandler = new CloudflareCookieHandler(logger) { InnerHandler = factoryHandler };
            var authHandler = new AuthHeaderHandler(options, logger) { InnerHandler = cookieHandler };
            var redirectHandler = new CloudflareAccessRedirectHandler(options, logger)
            {
                InnerHandler = authHandler,
            };

            logger?.LogDebug(
                "Created HTTP handler chain with {HeaderCount} headers: {HeaderNames}",
                defaultHeaders.Count,
                string.Join(", ", defaultHeaders.Keys)
            );

            // Dispose the factory client and create a new one with our handler chain
            // but preserve the timeout and other settings
            var timeout = factoryClient.Timeout;
            factoryClient.Dispose();

            httpClient = new HttpClient(redirectHandler) { BaseAddress = baseAddress, Timeout = timeout };

            logger?.LogDebug(
                "Created Refit client for {Type} with {HeaderCount} authentication headers and cookie support",
                typeof(T).Name,
                defaultHeaders.Count
            );
        }
        else
        {
            // No headers, use the factory client as-is
            httpClient = factoryClient;
        }

        return RestService.For<T>(httpClient, refitSettings);
    }

    /// <summary>
    /// Extracts the HttpMessageHandler from an HttpClient using reflection.
    /// This is needed to wrap the factory's handler chain with our custom handlers
    /// while preserving retry policies and other handler-level configurations.
    /// </summary>
    private static HttpMessageHandler GetHandlerFromHttpClient(
        HttpClient client,
        ILogger<ApiFactory>? logger = null
    )
    {
        // HttpClient stores its handler in a private field. We use reflection to extract it
        // so we can wrap it with our custom handlers while preserving the factory's configuration.
        var handlerField =
            typeof(HttpClient).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(HttpClient).GetField("handler", BindingFlags.NonPublic | BindingFlags.Instance);

        if (handlerField?.GetValue(client) is HttpMessageHandler factoryHandler)
        {
            return factoryHandler;
        }

        // Fallback: if we can't extract the handler, create a new HttpClientHandler
        // This should rarely happen, but ensures we don't break if HttpClient's internals change
        logger?.LogWarning(
            "Could not extract handler from factory HttpClient, using new HttpClientHandler as fallback"
        );
        return new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
        };
    }
}
