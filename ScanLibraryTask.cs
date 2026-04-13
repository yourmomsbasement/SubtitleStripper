using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleStripper;

/// <summary>
/// Scheduled task: runs once over the entire library.
/// Trigger manually from Dashboard → Scheduled Tasks → "Subtitle Stripper".
/// </summary>
public class ScanLibraryTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<ScanLibraryTask> _logger;

    public ScanLibraryTask(
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        ILogger<ScanLibraryTask> logger)
    {
        _libraryManager = libraryManager;
        _mediaEncoder   = mediaEncoder;
        _logger         = logger;
    }

    /// <inheritdoc />
    public string Name => "Strip Subtitles — Full Library Scan";

    /// <inheritdoc />
    public string Key => "SubtitleStripperFullScan";

    /// <inheritdoc />
    public string Description =>
        "Checks every video file in the library and removes embedded subtitle streams. " +
        "PGS streams are optionally converted to SRT sidecars first. " +
        "Run once for an initial sweep; new files are handled automatically via the item-added hook.";

    /// <inheritdoc />
    public string Category => "Subtitle Stripper";

    /// <inheritdoc />
    // No automatic schedule — the user triggers this manually the first time.
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Enumerable.Empty<TaskTriggerInfo>();

    private static Guid[] GetSelectedLibraryIds()
    {
        var raw = Plugin.Instance?.Configuration.SelectedLibraryIds ?? string.Empty;
        var ids = new List<Guid>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id))
                ids.Add(id);
        }
        return ids.ToArray();
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var processor = new SubtitleProcessor(_logger, _mediaEncoder);

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video],
            IsVirtualItem    = false,
            Recursive        = true
        };

        // Restrict to selected libraries if configured.
        var selectedIds = GetSelectedLibraryIds();
        if (selectedIds.Length > 0)
            query.AncestorIds = selectedIds;

        var items = _libraryManager.GetItemList(query)
        .OfType<Video>()
        .ToList();

        _logger.LogInformation("SubtitleStripper: Starting full scan — {Count} video items.", items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = items[i].Path;
            if (!string.IsNullOrEmpty(path))
                await processor.ProcessFileAsync(path, cancellationToken).ConfigureAwait(false);

            progress.Report(100.0 * (i + 1) / items.Count);
        }

        _logger.LogInformation("SubtitleStripper: Full scan complete.");
    }
}
