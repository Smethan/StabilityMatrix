using System.Collections.Generic;

namespace StabilityMatrix.Core.Models.Configs;

/// <summary>
/// Configuration settings for remote ComfyUI server authentication
/// </summary>
public class ComfyServerSettings
{
    /// <summary>
    /// Dictionary of custom headers to add to HTTP and WebSocket requests.
    /// Useful for authentication with services like Cloudflare tunnels.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }
}
