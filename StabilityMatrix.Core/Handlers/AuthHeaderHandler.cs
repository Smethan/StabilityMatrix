using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilityMatrix.Core.Models.Configs;

namespace StabilityMatrix.Core.Handlers;

/// <summary>
/// HTTP message handler that adds custom authentication headers to requests.
/// Used for authenticating with remote ComfyUI servers behind services like Cloudflare tunnels.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly ComfyServerSettings _settings;
    private readonly ILogger<AuthHeaderHandler>? _logger;

    public AuthHeaderHandler(
        IOptions<ComfyServerSettings> settings,
        ILogger<AuthHeaderHandler>? logger = null
    )
    {
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (_settings.Headers != null && _settings.Headers.Count > 0)
        {
            _logger?.LogDebug(
                "Adding {Count} authentication headers to request {Method} {Uri}",
                _settings.Headers.Count,
                request.Method,
                request.RequestUri
            );

            foreach (var header in _settings.Headers)
            {
                // Try to add as a request header first
                var added = request.Headers.TryAddWithoutValidation(header.Key, header.Value);

                if (!added)
                {
                    // If that fails, try adding to Content headers (for POST requests with content)
                    // Some headers might be restricted and need special handling
                    _logger?.LogWarning(
                        "Failed to add header '{HeaderName}' as request header, trying content headers",
                        header.Key
                    );

                    // For restricted headers like Authorization, we might need to use a different approach
                    // But most custom headers should work with TryAddWithoutValidation
                    if (request.Content != null)
                    {
                        added = request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    if (!added)
                    {
                        _logger?.LogError(
                            "Failed to add header '{HeaderName}' to request. "
                                + "This might be a restricted header or the header name/value format is invalid.",
                            header.Key
                        );
                    }
                }
                else
                {
                    _logger?.LogTrace("Successfully added header '{HeaderName}'", header.Key);
                }
            }

            // Log all headers that were added (for debugging)
            if (_logger?.IsEnabled(LogLevel.Trace) == true)
            {
                _logger.LogTrace(
                    "Request headers after adding auth headers: {Headers}",
                    string.Join(", ", request.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))
                );
            }
        }
        else
        {
            _logger?.LogDebug(
                "No authentication headers configured for request {Method} {Uri}",
                request.Method,
                request.RequestUri
            );
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
