using System.Net.Http;
using Microsoft.Extensions.Options;
using Refit;
using StabilityMatrix.Core.Handlers;
using StabilityMatrix.Core.Models.Configs;

namespace StabilityMatrix.Core.Api;

public class ApiFactory : IApiFactory
{
    private readonly IHttpClientFactory httpClientFactory;
    public RefitSettings? RefitSettings { get; init; }

    public ApiFactory(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
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

            // Create handler chain: AuthHeaderHandler -> HttpClientHandler
            var innerHandler = new HttpClientHandler();
            var authHandler = new AuthHeaderHandler(options) { InnerHandler = innerHandler };

            // Create HttpClient with the handler chain
            httpClient = new HttpClient(authHandler) { BaseAddress = baseAddress };
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
