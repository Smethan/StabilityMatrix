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
        HttpClient httpClient;

        // If headers are provided, use AuthHeaderHandler to properly inject them
        if (defaultHeaders != null && defaultHeaders.Count > 0)
        {
            // Create ComfyServerSettings with the headers
            var serverSettings = new ComfyServerSettings
            {
                Headers = new Dictionary<string, string>(defaultHeaders),
            };

            // Wrap in IOptions
            var options = Options.Create(serverSettings);

            // Create handler chain: CloudflareAccessRedirectHandler -> AuthHeaderHandler -> HttpClientHandler
            // This allows us to detect Cloudflare Access redirects and provide better error messages
            var innerHandler = new HttpClientHandler();
            var authHandler = new AuthHeaderHandler(options, logger) { InnerHandler = innerHandler };
            var redirectHandler = new CloudflareAccessRedirectHandler(options, logger)
            {
                InnerHandler = authHandler,
            };

            logger?.LogDebug(
                "Created HTTP handler chain with {HeaderCount} headers: {HeaderNames}",
                defaultHeaders.Count,
                string.Join(", ", defaultHeaders.Keys)
            );

            // Create HttpClient with the handler chain
            httpClient = new HttpClient(redirectHandler) { BaseAddress = baseAddress };

            logger?.LogDebug(
                "Created Refit client for {Type} with {HeaderCount} authentication headers",
                typeof(T).Name,
                defaultHeaders.Count
            );
        }
        else
        {
            // No headers, use the factory as before
            httpClient = httpClientFactory.CreateClient(nameof(T));
            httpClient.BaseAddress = baseAddress;
        }

        return RestService.For<T>(httpClient, refitSettings);
    }
}
