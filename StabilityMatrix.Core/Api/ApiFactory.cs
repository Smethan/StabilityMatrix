using System.Net.Http;
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
    private readonly ILoggerFactory? loggerFactory;
    public RefitSettings? RefitSettings { get; init; }

    public ApiFactory(
        IHttpClientFactory httpClientFactory,
        ILogger<ApiFactory>? logger = null,
        ILoggerFactory? loggerFactory = null
    )
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
        this.loggerFactory = loggerFactory;
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
        HttpClient httpClient;

        // If headers are provided, we need to create a custom handler chain
        if (defaultHeaders != null && defaultHeaders.Count > 0)
        {
            logger?.LogDebug(
                "Creating Refit client for {Type} with {HeaderCount} custom headers: {HeaderNames}",
                typeof(T).Name,
                defaultHeaders.Count,
                string.Join(", ", defaultHeaders.Keys)
            );

            // Create ComfyServerSettings with the headers
            var serverSettings = new ComfyServerSettings
            {
                Headers = new Dictionary<string, string>(defaultHeaders),
            };

            // Wrap in IOptions
            var options = Options.Create(serverSettings);

            // Create typed loggers for each handler
            var cookieLogger = loggerFactory?.CreateLogger<CloudflareCookieHandler>();
            var authLogger = loggerFactory?.CreateLogger<AuthHeaderHandler>();
            var redirectLogger = loggerFactory?.CreateLogger<CloudflareAccessRedirectHandler>();

            // Create the base handler with cookie support
            // This will be the innermost handler that actually sends HTTP requests
            var baseHandler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer(),
            };

            // Build handler chain from innermost to outermost:
            // 1. HttpClientHandler (baseHandler) - sends requests, handles cookies
            // 2. CloudflareCookieHandler - logs cookie activity
            // 3. AuthHeaderHandler - adds custom headers
            // 4. CloudflareAccessRedirectHandler - detects redirects
            var cookieHandler = new CloudflareCookieHandler(cookieLogger) { InnerHandler = baseHandler };
            var authHandler = new AuthHeaderHandler(options, authLogger) { InnerHandler = cookieHandler };
            var redirectHandler = new CloudflareAccessRedirectHandler(options, redirectLogger)
            {
                InnerHandler = authHandler,
            };

            // Create HttpClient with our custom handler chain
            // Use a reasonable default timeout (60 seconds) since we're not using factory client
            httpClient = new HttpClient(redirectHandler)
            {
                BaseAddress = baseAddress,
                Timeout = TimeSpan.FromSeconds(60),
            };

            logger?.LogDebug(
                "Created Refit client for {Type} with custom handler chain and {HeaderCount} authentication headers",
                typeof(T).Name,
                defaultHeaders.Count
            );
        }
        else
        {
            // No headers, use the factory client as-is
            httpClient = httpClientFactory.CreateClient(nameof(T));
            httpClient.BaseAddress = baseAddress;
        }

        return RestService.For<T>(httpClient, refitSettings);
    }
}
