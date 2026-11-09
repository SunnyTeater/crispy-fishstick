using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomBranding;

/// <summary>
/// Die austauschbaren Bilder des Webclients.
/// </summary>
public enum BrandingSlot
{
    /// <summary>Helles Banner, wird auf dunklen Themes angezeigt (banner-light).</summary>
    BannerLight,

    /// <summary>Dunkles Banner, wird auf hellen Themes angezeigt (banner-dark).</summary>
    BannerDark,

    /// <summary>Quadratisches Icon (icon-transparent): Menue, Ladebildschirm, TV-Layout.</summary>
    Icon,

    /// <summary>Favicon samt Touch-Icons.</summary>
    Favicon
}

/// <summary>
/// Zustand eines Bildes.
/// </summary>
/// <param name="Slot">Name des Slots.</param>
/// <param name="HasCustom">Ob ein eigenes Bild hinterlegt ist.</param>
/// <param name="Files">Die betroffenen Dateien relativ zum Web-Ordner.</param>
public sealed record SlotStatus(string Slot, bool HasCustom, IReadOnlyList<string> Files);

/// <summary>
/// Gesamtzustand fuer die Konfigurationsseite.
/// </summary>
/// <param name="WebPath">Der Web-Ordner des Servers.</param>
/// <param name="Writable">Ob der Server dort schreiben darf.</param>
/// <param name="Slots">Zustand je Bild.</param>
public sealed record BrandingStatus(string WebPath, bool Writable, IReadOnlyList<SlotStatus> Slots);

/// <summary>
/// Ueberschreibt Logo- und Favicon-Dateien im Web-Ordner und sichert vorher die Originale.
/// </summary>
public sealed class BrandingService
{
    /// <summary>
    /// Maximale Groesse eines Uploads in Bytes.
    /// </summary>
    public const int MaxFileSize = 5 * 1024 * 1024;

    private static readonly byte[] _pngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] _icoMagic = { 0x00, 0x00, 0x01, 0x00 };

    // Markiert in state.json eine Datei, deren Ueberschreiben begonnen, aber nicht bestaetigt wurde.
    private const string PendingPrefix = "pending:";

    // Der Webclient wird mit Hash im Dateinamen gebaut (z. B. banner-light.1a2b3c.png),
    // deshalb wird ueber Muster gesucht statt ueber feste Namen.
    private static readonly Dictionary<BrandingSlot, Target[]> _targets = new()
    {
        [BrandingSlot.BannerLight] = new[] { new Target(string.Empty, "banner-light*.png", false) },
        [BrandingSlot.BannerDark] = new[] { new Target(string.Empty, "banner-dark*.png", false) },
        [BrandingSlot.Icon] = new[] { new Target(string.Empty, "icon-transparent*.png", false) },
        [BrandingSlot.Favicon] = new[]
        {
            new Target(string.Empty, "favicon*.ico", true),
            new Target(string.Empty, "favicon*.png", false),
            new Target(string.Empty, "touchicon*.png", false),
            new Target("favicons", "*.ico", true),
            new Target("favicons", "*.png", false)
        }
    };

    private readonly object _lock = new();
    private readonly ILogger<BrandingService> _logger;
    private readonly string _webPath;
    private readonly string _customDir;
    private readonly string _backupDir;
    private readonly string _stateFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrandingService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Pfade des Servers.</param>
    /// <param name="logger">Der Logger.</param>
    public BrandingService(IApplicationPaths applicationPaths, ILogger<BrandingService> logger)
    {
        _logger = logger;
        _webPath = applicationPaths.WebPath;

        // Bewusst nicht im Plugin-Ordner: der wird bei einem Plugin-Update ersetzt.
        var dataDir = Path.Combine(applicationPaths.DataPath, "custom-branding");
        _customDir = Path.Combine(dataDir, "custom");
        _backupDir = Path.Combine(dataDir, "backup");
        _stateFile = Path.Combine(dataDir, "state.json");
    }

    /// <summary>
    /// Liefert den aktuellen Zustand.
    /// </summary>
    /// <returns>Der Zustand aller Bilder.</returns>
    public BrandingStatus GetStatus()
    {
        lock (_lock)
        {
            var slots = new List<SlotStatus>();
            foreach (var slot in Enum.GetValues<BrandingSlot>())
            {
                var files = FindTargets(slot).Select(t => t.RelativePath).ToList();
                slots.Add(new SlotStatus(slot.ToString(), FindCustomFile(slot) is not null, files));
            }

            return new BrandingStatus(_webPath, IsWritable(), slots);
        }
    }

    /// <summary>
    /// Speichert ein hochgeladenes Bild und ueberschreibt damit die Originale.
    /// </summary>
    /// <param name="slot">Das zu ersetzende Bild.</param>
    /// <param name="data">Der Dateiinhalt.</param>
    /// <exception cref="ArgumentException">Die Datei hat kein erlaubtes Format.</exception>
    public void SetCustom(BrandingSlot slot, byte[] data)
    {
        var isPng = StartsWith(data, _pngMagic);
        var isIco = StartsWith(data, _icoMagic);

        if (slot == BrandingSlot.Favicon)
        {
            if (!isPng && !isIco)
            {
                throw new ArgumentException("Das Favicon muss eine PNG- oder ICO-Datei sein.");
            }
        }
        else if (!isPng)
        {
            // Die Zieldateien heissen *.png und werden als image/png ausgeliefert.
            throw new ArgumentException("Das Bild muss eine PNG-Datei sein.");
        }

        lock (_lock)
        {
            // Erst die Originale zurueckspielen: ein ICO-Upload ersetzt weniger Dateien als ein PNG-Upload.
            Restore(slot);
            DeleteCustomFiles(slot);

            Directory.CreateDirectory(_customDir);
            File.WriteAllBytes(Path.Combine(_customDir, slot + (isIco ? ".ico" : ".png")), data);

            try
            {
                Apply(slot);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Nichts halb Eingespieltes zuruecklassen: sonst gaelte das Bild als ersetzt, obwohl es das nicht ist.
                try
                {
                    Restore(slot);
                }
                catch (Exception restoreEx) when (restoreEx is UnauthorizedAccessException or IOException)
                {
                    _logger.LogWarning(restoreEx, "Custom Branding: Originale konnten nach fehlgeschlagenem Upload nicht zurueckgespielt werden");
                }

                DeleteCustomFiles(slot);
                throw;
            }
        }
    }

    /// <summary>
    /// Entfernt das eigene Bild und stellt die Originale wieder her.
    /// </summary>
    /// <param name="slot">Das zurueckzusetzende Bild.</param>
    public void Reset(BrandingSlot slot)
    {
        lock (_lock)
        {
            Restore(slot);
            DeleteCustomFiles(slot);
        }
    }

    /// <summary>
    /// Spielt alle hinterlegten Bilder (erneut) in den Web-Ordner ein.
    /// </summary>
    /// <returns>Anzahl der geschriebenen Dateien.</returns>
    public int ApplyAll()
    {
        lock (_lock)
        {
            var written = 0;
            foreach (var slot in Enum.GetValues<BrandingSlot>())
            {
                written += Apply(slot);
            }

            return written;
        }
    }

    private int Apply(BrandingSlot slot)
    {
        var customFile = FindCustomFile(slot);
        if (customFile is null)
        {
            return 0;
        }

        var custom = File.ReadAllBytes(customFile);
        var customIsIco = StartsWith(custom, _icoMagic);

        // Ein PNG wird fuer die .ico-Dateien in einen ICO-Container gepackt.
        // Wurde direkt ein ICO hochgeladen, bleiben die PNG-Touch-Icons unveraendert.
        var icoContent = customIsIco ? custom : WrapPngInIco(custom);
        var pngContent = customIsIco ? null : custom;

        var state = LoadState();
        var written = 0;

        foreach (var (fullPath, relativePath, isIco) in FindTargets(slot))
        {
            var content = isIco ? icoContent : pngContent;
            if (content is null)
            {
                continue;
            }

            var contentHash = Hash(content);
            var currentHash = Hash(File.ReadAllBytes(fullPath));
            if (currentHash == contentHash)
            {
                state[relativePath] = contentHash;
                continue;
            }

            // Stammt die Datei nicht von uns, ist sie ein Original (z. B. nach einem Update) und wird gesichert.
            // Eine als "pending" markierte Datei ist ein abgebrochener Schreibversuch von uns: sie darf
            // die Sicherung des Originals nicht ueberschreiben.
            var isOurs = state.TryGetValue(relativePath, out var lastWritten)
                && (lastWritten == currentHash || IsPending(lastWritten));
            if (!isOurs)
            {
                var backupPath = Path.Combine(_backupDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(fullPath, backupPath, true);
            }

            // Vor dem Schreiben vermerken, dass diese Datei gleich von uns stammt. Bricht der Server
            // mitten im Schreiben ab, bleibt der Vermerk stehen und die Sicherung beim naechsten Start erhalten.
            state.TryGetValue(relativePath, out var previousState);
            state[relativePath] = PendingPrefix + contentHash;
            SaveState(state);

            try
            {
                File.WriteAllBytes(fullPath, content);
            }
            catch (UnauthorizedAccessException)
            {
                // Fehlende Schreibrechte: die Datei wurde gar nicht angefasst, der Vermerk waere falsch.
                if (previousState is null)
                {
                    state.Remove(relativePath);
                }
                else
                {
                    state[relativePath] = previousState;
                }

                SaveState(state);
                throw;
            }

            state[relativePath] = contentHash;
            written++;
            _logger.LogDebug("Custom Branding: {File} ersetzt", relativePath);
        }

        SaveState(state);
        return written;
    }

    private void Restore(BrandingSlot slot)
    {
        var state = LoadState();
        var changed = false;

        foreach (var relativePath in state.Keys.ToList())
        {
            if (!Matches(slot, relativePath))
            {
                continue;
            }

            var fullPath = Path.Combine(_webPath, relativePath);
            var backupPath = Path.Combine(_backupDir, relativePath);

            if (File.Exists(backupPath))
            {
                // Nur zurueckspielen, wenn dort noch unser Bild liegt. Nach einem Jellyfin-Update
                // liegt bereits ein neues Original vor, das nicht durch ein altes ersetzt werden darf.
                if (File.Exists(fullPath) && !IsLink(fullPath))
                {
                    var currentHash = Hash(File.ReadAllBytes(fullPath));
                    var isOurs = IsPending(state[relativePath]) || currentHash == state[relativePath];

                    // Liegt dort bereits das Original, gibt es nichts zurueckzuschreiben.
                    if (isOurs && currentHash != Hash(File.ReadAllBytes(backupPath)))
                    {
                        File.Copy(backupPath, fullPath, true);
                    }
                }

                File.Delete(backupPath);
            }

            state.Remove(relativePath);
            changed = true;
        }

        if (changed)
        {
            SaveState(state);
        }
    }

    private List<(string FullPath, string RelativePath, bool IsIco)> FindTargets(BrandingSlot slot)
    {
        var result = new List<(string FullPath, string RelativePath, bool IsIco)>();
        foreach (var target in _targets[slot])
        {
            var dir = Path.Combine(_webPath, target.SubDir);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, target.Pattern, SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                // Symlinks nicht anfassen: sonst wuerde das Ziel des Links ueberschrieben,
                // das ausserhalb des Web-Ordners liegen kann.
                if (IsLink(file))
                {
                    continue;
                }

                var name = Path.GetFileName(file);
                var relativePath = target.SubDir.Length == 0 ? name : target.SubDir + "/" + name;
                result.Add((file, relativePath, target.IsIco));
            }
        }

        return result;
    }

    private static bool Matches(BrandingSlot slot, string relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        var subDir = separator < 0 ? string.Empty : relativePath[..separator];
        var name = relativePath[(separator + 1)..];

        return _targets[slot].Any(t =>
            string.Equals(t.SubDir, subDir, StringComparison.Ordinal)
            && FileSystemName.MatchesSimpleExpression(t.Pattern, name, true));
    }

    private string? FindCustomFile(BrandingSlot slot)
    {
        foreach (var extension in new[] { ".png", ".ico" })
        {
            var path = Path.Combine(_customDir, slot + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private void DeleteCustomFiles(BrandingSlot slot)
    {
        // File.Delete wirft, wenn der Ordner noch nicht existiert (erster Upload).
        if (!Directory.Exists(_customDir))
        {
            return;
        }

        File.Delete(Path.Combine(_customDir, slot + ".png"));
        File.Delete(Path.Combine(_customDir, slot + ".ico"));
    }

    private bool IsWritable()
    {
        try
        {
            var probe = Enum.GetValues<BrandingSlot>().SelectMany(FindTargets).Select(t => t.FullPath).FirstOrDefault();
            if (probe is null)
            {
                return false;
            }

            // Oeffnet die Datei nur zum Schreiben, veraendert sie aber nicht.
            using var stream = new FileStream(probe, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private Dictionary<string, string> LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_stateFile))
                    ?? new Dictionary<string, string>();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Custom Branding: state.json ist beschaedigt und wird neu angelegt");
        }

        return new Dictionary<string, string>();
    }

    private void SaveState(Dictionary<string, string> state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
        File.WriteAllText(_stateFile, JsonSerializer.Serialize(state));
    }

    private static bool StartsWith(byte[] data, byte[] magic)
        => data.Length > magic.Length && data.AsSpan(0, magic.Length).SequenceEqual(magic);

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static bool IsPending(string stateValue) => stateValue.StartsWith(PendingPrefix, StringComparison.Ordinal);

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    /// <summary>
    /// Packt ein PNG unveraendert in einen ICO-Container (ICO darf PNG-Daten enthalten).
    /// </summary>
    private static byte[] WrapPngInIco(byte[] png)
    {
        // Breite und Hoehe stehen im IHDR-Chunk als Big-Endian ab Offset 16 bzw. 20.
        var width = png.Length >= 24 ? (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19] : 0;
        var height = png.Length >= 24 ? (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23] : 0;

        using var stream = new MemoryStream(22 + png.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);                              // reserviert
        writer.Write((ushort)1);                              // Typ: Icon
        writer.Write((ushort)1);                              // Anzahl Bilder
        writer.Write((byte)(width is > 0 and < 256 ? width : 0));   // 0 bedeutet 256
        writer.Write((byte)(height is > 0 and < 256 ? height : 0));
        writer.Write((byte)0);                                // Farbpalette
        writer.Write((byte)0);                                // reserviert
        writer.Write((ushort)1);                              // Farbebenen
        writer.Write((ushort)32);                             // Bits pro Pixel
        writer.Write((uint)png.Length);                       // Groesse der Bilddaten
        writer.Write((uint)22);                               // Offset der Bilddaten
        writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }

    private sealed record Target(string SubDir, string Pattern, bool IsIco);
}
