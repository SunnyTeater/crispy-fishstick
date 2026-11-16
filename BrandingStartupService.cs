using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomBranding;

/// <summary>
/// Spielt die eigenen Bilder bei jedem Serverstart erneut ein.
/// Noetig, weil ein Jellyfin-Update den Web-Ordner mit den Originalen ueberschreibt.
/// </summary>
public class BrandingStartupService : IHostedService
{
    private readonly BrandingService _branding;
    private readonly ILogger<BrandingStartupService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrandingStartupService"/> class.
    /// </summary>
    /// <param name="branding">Der Branding-Dienst.</param>
    /// <param name="logger">Der Logger.</param>
    public BrandingStartupService(BrandingService branding, ILogger<BrandingStartupService> logger)
    {
        _branding = branding;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var written = _branding.ApplyAll();
            if (written > 0)
            {
                _logger.LogInformation("Custom Branding: {Count} Datei(en) im Web-Ordner ersetzt", written);
            }
        }
        catch (Exception ex)
        {
            // Ein Fehler hier darf den Serverstart nicht verhindern.
            _logger.LogError(ex, "Custom Branding: Bilder konnten beim Start nicht eingespielt werden");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
