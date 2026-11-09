using System;
using System.Collections.Generic;
using Jellyfin.Plugin.CustomBranding.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CustomBranding;

/// <summary>
/// Ersetzt Logo und Favicon des Jellyfin-Webclients.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Pfade des Servers.</param>
    /// <param name="xmlSerializer">XML-Serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => "Custom Branding";

    /// <inheritdoc />
    public override string Description => "Ersetzt Logo und Favicon des Webclients durch eigene Dateien.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("df0a51c3-7630-48b5-ab2c-e7fdd3778e57");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "CustomBranding",
                DisplayName = "Custom Branding",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuIcon = "image"
            }
        };
    }
}
