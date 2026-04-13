using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleStripper;

/// <summary>
/// Listens for library item-added events and processes new video files automatically.
/// Registered as a hosted service so it starts with Jellyfin and stops cleanly.
/// </summary>
public sealed class ItemAddedConsumer : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<ItemAddedConsumer> _logger;

    public ItemAddedConsumer(
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        ILogger<ItemAddedConsumer> logger)
    {
        _libraryManager = libraryManager;
        _mediaEncoder   = mediaEncoder;
        _logger         = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _logger.LogInformation("SubtitleStripper: Listening for new library items.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (Plugin.Instance?.Configuration.ProcessOnItemAdded != true)
            return;

        // Only care about real video files — skip virtual items, folders, etc.
        if (e.Item is not Video video)
            return;

        var path = video.Path;
        if (string.IsNullOrEmpty(path))
            return;

        if (!SubtitleProcessor.IsSupportedFile(path))
            return;

        // If specific libraries are configured, only process items that belong to them.
        var selectedIds = GetSelectedLibraryIds();
        if (selectedIds.Length > 0)
        {
            var folders = _libraryManager.GetCollectionFolders(video);
            if (!folders.Any(f => System.Array.IndexOf(selectedIds, f.Id) >= 0))
                return;
        }

        // Fire-and-forget with a captured logger so we can report errors.
        var logger    = _logger;
        var encoder   = _mediaEncoder;

        _ = Task.Run(async () =>
        {
            // Small delay: give Jellyfin time to finish writing metadata before
            // we potentially rewrite the file.
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            try
            {
                var processor = new SubtitleProcessor(logger, encoder);
                await processor.ProcessFileAsync(path, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SubtitleStripper: Unhandled error processing {Path}", path);
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
        => _libraryManager.ItemAdded -= OnItemAdded;

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
}
