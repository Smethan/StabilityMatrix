using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

    public AuthHeaderHandler(IOptions<ComfyServerSettings> settings)
    {
        _settings = settings.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (_settings.Headers != null)
        {
            foreach (var header in _settings.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
