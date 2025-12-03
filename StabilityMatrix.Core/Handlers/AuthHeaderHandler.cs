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

            var addedHeaders = new List<string>();
            var failedHeaders = new List<string>();

            foreach (var header in _settings.Headers)
            {
                // Validate header name and value
                if (string.IsNullOrWhiteSpace(header.Key))
                {
                    _logger?.LogWarning("Skipping header with empty key");
                    failedHeaders.Add("(empty key)");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(header.Value))
                {
                    _logger?.LogWarning("Skipping header '{HeaderName}' with empty value", header.Key);
                    failedHeaders.Add(header.Key);
                    continue;
                }

                // Try to add as a request header first
                // Most custom headers (like CF-Access-Token) should work with TryAddWithoutValidation
                var added = request.Headers.TryAddWithoutValidation(header.Key, header.Value);

                if (!added)
                {
                    // Check if it's a restricted header that needs special handling
                    // Restricted headers include: Host, Content-Length, Connection, etc.
                    // Custom headers like CF-Access-Token should NOT be restricted
                    var isRestrictedHeader = IsRestrictedHeader(header.Key);

                    if (isRestrictedHeader)
                    {
                        _logger?.LogWarning(
                            "Header '{HeaderName}' is a restricted header and cannot be added via TryAddWithoutValidation. "
                                + "This header may need to be set via HttpClient.DefaultRequestHeaders or a different mechanism.",
                            header.Key
                        );
                        failedHeaders.Add(header.Key);
                    }
                    else
                    {
                        // For non-restricted headers that fail, this is unexpected
                        // Try adding to Content headers as a fallback (though this usually won't work for request headers)
                        if (request.Content != null)
                        {
                            added = request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }

                        if (!added)
                        {
                            _logger?.LogError(
                                "Failed to add header '{HeaderName}' to request. "
                                    + "Header name/value format may be invalid, or the header may already be present.",
                                header.Key
                            );
                            failedHeaders.Add(header.Key);
                        }
                        else
                        {
                            _logger?.LogWarning(
                                "Added header '{HeaderName}' to content headers (unusual for request headers)",
                                header.Key
                            );
                            addedHeaders.Add(header.Key);
                        }
                    }
                }
                else
                {
                    addedHeaders.Add(header.Key);
                    _logger?.LogTrace("Successfully added header '{HeaderName}'", header.Key);
                }
            }

            // Log summary
            if (failedHeaders.Count > 0)
            {
                _logger?.LogError(
                    "Failed to add {FailedCount} header(s): {FailedHeaders}. "
                        + "Requests may fail authentication. Check header names and values.",
                    failedHeaders.Count,
                    string.Join(", ", failedHeaders)
                );
            }

            if (addedHeaders.Count > 0)
            {
                _logger?.LogDebug(
                    "Successfully added {AddedCount} header(s): {AddedHeaders}",
                    addedHeaders.Count,
                    string.Join(", ", addedHeaders)
                );
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

    /// <summary>
    /// Checks if a header name is a restricted header that cannot be set via TryAddWithoutValidation.
    /// Restricted headers are managed by HttpClient/HttpClientHandler and cannot be modified.
    /// </summary>
    private static bool IsRestrictedHeader(string headerName)
    {
        // Common restricted headers that HttpClient manages automatically
        var restrictedHeaders = new[]
        {
            "Host",
            "Connection",
            "Content-Length",
            "Transfer-Encoding",
            "Upgrade",
            "Proxy-Connection",
            "Keep-Alive",
            "TE",
            "Trailer",
        };

        return restrictedHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
    }
}
