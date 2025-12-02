using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace StabilityMatrix.Core.Handlers;

/// <summary>
/// HTTP message handler that logs Cloudflare Access cookie activity.
/// Note: HttpClientHandler with UseCookies=true handles cookies automatically,
/// but this handler provides visibility into cookie capture for debugging.
/// </summary>
public class CloudflareCookieHandler : DelegatingHandler
{
    private readonly ILogger<CloudflareCookieHandler>? _logger;

    public CloudflareCookieHandler(ILogger<CloudflareCookieHandler>? logger = null)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        // Log cookies being sent (if any)
        if (request.Headers.TryGetValues("Cookie", out var cookieHeaders))
        {
            var cookies = string.Join("; ", cookieHeaders);
            _logger?.LogTrace(
                "Sending cookies with request {Method} {Uri}: {Cookies}",
                request.Method,
                request.RequestUri,
                cookies
            );
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Log cookies being received (HttpClientHandler will handle them automatically)
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            foreach (var setCookieHeader in setCookieHeaders)
            {
                // Extract cookie name
                var cookieName = setCookieHeader.Split(';')[0].Split('=')[0].Trim();

                _logger?.LogDebug(
                    "Received Set-Cookie header '{CookieName}' from response {StatusCode} {Uri}",
                    cookieName,
                    response.StatusCode,
                    response.RequestMessage?.RequestUri ?? request.RequestUri
                );

                // Special logging for CF_Authorization cookie
                if (cookieName.Equals("CF_Authorization", System.StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation(
                        "Captured CF_Authorization cookie from Cloudflare Access response. "
                            + "HttpClientHandler will automatically include this cookie in subsequent requests to {BaseUri}",
                        request.RequestUri?.GetLeftPart(System.UriPartial.Authority)
                    );
                }
            }
        }

        return response;
    }
}
