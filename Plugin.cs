using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubtitleStripper;

/// <summary>
/// Main plugin entry-point. Jellyfin discovers this via reflection.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Subtitle Stripper";

    /// <inheritdoc />
    public override Guid Id => new Guid("f3e2d1c0-b9a8-7654-3210-fedcba987654");

    /// <inheritdoc />
    public override string Description => "Removes embedded subtitle streams from video files using ffmpeg.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name        = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        }
    ];
}
