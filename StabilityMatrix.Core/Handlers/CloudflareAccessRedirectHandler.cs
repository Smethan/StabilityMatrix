using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilityMatrix.Core.Models.Configs;

namespace StabilityMatrix.Core.Handlers;

/// <summary>
/// HTTP message handler that detects redirects to Cloudflare Access login pages.
/// This helps provide better error messages when authentication fails.
/// Note: We can't prevent the redirect here since HttpClientHandler handles redirects automatically,
/// but we can detect it and add metadata to help with error handling.
/// </summary>
public class CloudflareAccessRedirectHandler : DelegatingHandler
{
    private readonly ILogger<CloudflareAccessRedirectHandler>? _logger;

    public CloudflareAccessRedirectHandler(
        IOptions<ComfyServerSettings> settings,
        ILogger<CloudflareAccessRedirectHandler>? logger = null
    )
    {
        _logger = logger;
        // Note: settings parameter kept for consistency with handler chain, but not currently used
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        // Log request headers before sending (for debugging)
        if (_logger?.IsEnabled(LogLevel.Trace) == true && request.Headers.Any())
        {
            _logger.LogTrace(
                "Request headers before sending {Method} {Uri}: {Headers}",
                request.Method,
                request.RequestUri,
                string.Join(", ", request.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))
            );
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Check if we were redirected to a Cloudflare Access login page
        // This happens when authentication headers are missing or incorrect
        if (
            response.RequestMessage?.RequestUri != null
            && response.RequestMessage.RequestUri.Host.Contains(
                "cloudflareaccess.com",
                System.StringComparison.OrdinalIgnoreCase
            )
            && response.RequestMessage.RequestUri != request.RequestUri
        )
        {
            // Log what headers were actually sent (from the final request message after redirects)
            var sentHeaders = response.RequestMessage.Headers.Any()
                ? string.Join(
                    ", ",
                    response.RequestMessage.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
                )
                : "none";

            _logger?.LogWarning(
                "Request to {OriginalUri} was redirected to Cloudflare Access login page: {RedirectUri}. "
                    + "This usually means authentication headers are missing or incorrect. "
                    + "Check your ComfyUI authentication headers configuration. "
                    + "Cloudflare Access typically requires headers like CF-Access-Token or CF-Access-Client-Id/Secret. "
                    + "Headers sent in request: {SentHeaders}",
                request.RequestUri,
                response.RequestMessage.RequestUri,
                sentHeaders
            );
        }

        return response;
    }
}
