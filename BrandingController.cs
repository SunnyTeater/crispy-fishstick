using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CustomBranding.Api;

/// <summary>
/// API fuer die Konfigurationsseite. Nur fuer Administratoren.
/// </summary>
[ApiController]
[Route("CustomBranding")]
[Authorize(Policy = Policies.RequiresElevation)]
public class BrandingController : ControllerBase
{
    private const string NotWritableMessage =
        "Der Web-Ordner ist fuer den Jellyfin-Dienst nicht beschreibbar. Schreibrechte vergeben und erneut versuchen.";

    private readonly BrandingService _branding;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrandingController"/> class.
    /// </summary>
    /// <param name="branding">Der Branding-Dienst.</param>
    public BrandingController(BrandingService branding)
    {
        _branding = branding;
    }

    /// <summary>
    /// Liefert den aktuellen Zustand.
    /// </summary>
    /// <returns>Der Zustand aller Bilder.</returns>
    [HttpGet("Status")]
    public ActionResult<BrandingStatus> GetStatus() => _branding.GetStatus();

    /// <summary>
    /// Laedt ein Bild hoch (Rohdaten im Request-Body) und ersetzt damit die Originale.
    /// </summary>
    /// <param name="slot">Das zu ersetzende Bild.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    /// <returns>Der neue Zustand.</returns>
    [HttpPost("Image/{slot}")]
    [RequestSizeLimit(BrandingService.MaxFileSize)]
    public async Task<ActionResult> Upload([FromRoute] BrandingSlot slot, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(slot))
        {
            return BadRequest(new { Message = "Unbekanntes Bild." });
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > BrandingService.MaxFileSize)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { Message = "Die Datei ist groesser als 5 MB." });
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            _branding.SetCustom(slot, buffer.ToArray());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = NotWritableMessage + " (" + ex.Message + ")" });
        }

        return Ok(_branding.GetStatus());
    }

    /// <summary>
    /// Entfernt das eigene Bild und stellt die Originale wieder her.
    /// </summary>
    /// <param name="slot">Das zurueckzusetzende Bild.</param>
    /// <returns>Der neue Zustand.</returns>
    [HttpDelete("Image/{slot}")]
    public ActionResult Reset([FromRoute] BrandingSlot slot)
    {
        if (!Enum.IsDefined(slot))
        {
            return BadRequest(new { Message = "Unbekanntes Bild." });
        }

        try
        {
            _branding.Reset(slot);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = NotWritableMessage + " (" + ex.Message + ")" });
        }

        return Ok(_branding.GetStatus());
    }
}
