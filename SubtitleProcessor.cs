using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleStripper;

/// <summary>
/// Core processing pipeline for a single video file:
///   1. (optional) PGS → SRT conversion via <see cref="PgsConverter"/>
///   2. Strip all embedded subtitle streams with ffmpeg
/// </summary>
public sealed class SubtitleProcessor
{
    private readonly ILogger _logger;
    private readonly IMediaEncoder _mediaEncoder;

    private static readonly string[] SupportedExtensions =
        [".mkv", ".mp4", ".m4v", ".avi", ".mov"];

    public SubtitleProcessor(ILogger logger, IMediaEncoder mediaEncoder)
    {
        _logger       = logger;
        _mediaEncoder = mediaEncoder;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private (string Ffmpeg, string Ffprobe) GetBinaries()
    {
        var configured = Plugin.Instance?.Configuration.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var dir = Path.GetDirectoryName(configured) ?? string.Empty;
            return (configured, Path.Combine(dir, "ffprobe"));
        }
        return (_mediaEncoder.EncoderPath, _mediaEncoder.ProbePath);
    }

    /// <summary>Runs a process; returns (stdout, stderr, exitCode).</summary>
    internal static async Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(
        string executable, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = executable,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close(); // never read from stdin

        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return (stdout, stderr, process.ExitCode);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public static bool IsSupportedFile(string path)
        => Array.IndexOf(SupportedExtensions,
               Path.GetExtension(path).ToLowerInvariant()) >= 0;

    public async Task<bool> HasSubtitleStreamsAsync(string path, CancellationToken ct)
    {
        var (_, ffprobe) = GetBinaries();
        var (stdout, _, _) = await RunAsync(ffprobe,
        [
            "-v", "error",
            "-select_streams", "s",
            "-show_entries", "stream=index",
            "-of", "csv=p=0",
            "--", path
        ], ct).ConfigureAwait(false);

        return !string.IsNullOrWhiteSpace(stdout);
    }

    /// <summary>
    /// Full pipeline: optionally convert PGS subtitles to SRT sidecars, then
    /// strip all embedded subtitle streams from the video.
    /// Returns <c>true</c> on success or when no action was needed.
    /// </summary>
    public async Task<bool> ProcessFileAsync(string inputPath, CancellationToken ct)
    {
        if (!IsSupportedFile(inputPath)) return true;

        var fileName = Path.GetFileName(inputPath);
        if (fileName.Contains(".nosubs_tmp.") || fileName.Contains(".bak."))
            return true;

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var (ffmpeg, ffprobe) = GetBinaries();

        var dir    = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var ext    = Path.GetExtension(inputPath);
        var stem   = Path.GetFileNameWithoutExtension(inputPath);
        var temp   = Path.Combine(dir, $"{stem}.nosubs_tmp{ext}");
        var backup = Path.Combine(dir, $"{stem}.bak{ext}");

        _logger.LogInformation("SubtitleStripper: Checking {Path}", inputPath);

        // ── Step 1: PGS → SRT (optional) ────────────────────────────────────
        if (config.ConvertPgsToSrt)
        {
            var converter = new PgsConverter(_logger, ffprobe);
            var converted = await converter.ConvertAsync(inputPath, ct).ConfigureAwait(false);
            if (converted)
                _logger.LogInformation(
                    "SubtitleStripper: PGS conversion completed for {Path}", inputPath);
        }

        // ── Step 2: Check whether any subtitle streams remain ────────────────
        if (!await HasSubtitleStreamsAsync(inputPath, ct).ConfigureAwait(false))
        {
            _logger.LogDebug("SubtitleStripper: No subtitle streams — skipping strip for {Path}",
                inputPath);
            return true;
        }

        _logger.LogInformation("SubtitleStripper: Stripping subtitle streams from {Path}", inputPath);

        if (config.DryRun)
        {
            _logger.LogInformation("SubtitleStripper: DRY RUN — would strip {Path}", inputPath);
            return true;
        }

        // ── Step 3: Strip subtitles with ffmpeg ──────────────────────────────
        if (File.Exists(temp)) File.Delete(temp);

        var (_, ffmpegErr, exitCode) = await RunAsync(ffmpeg,
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-nostdin",
            "-i",   inputPath,
            "-map", "0",
            "-map", "-0:s",
            "-c",   "copy",
            "--",   temp
        ], ct).ConfigureAwait(false);

        if (exitCode != 0)
        {
            _logger.LogError("SubtitleStripper: ffmpeg failed for {Path}: {Error}",
                inputPath, ffmpegErr);
            if (File.Exists(temp)) File.Delete(temp);
            return false;
        }

        var tempInfo = new FileInfo(temp);
        if (!tempInfo.Exists || tempInfo.Length == 0)
        {
            _logger.LogError("SubtitleStripper: Output missing or empty: {Temp}", temp);
            if (File.Exists(temp)) File.Delete(temp);
            return false;
        }

        // Validate output still has a video stream.
        var (videoOut, _, videoExit) = await RunAsync(ffprobe,
        [
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=codec_name",
            "-of", "default=nw=1:nk=1",
            "--", temp
        ], ct).ConfigureAwait(false);

        if (videoExit != 0 || string.IsNullOrWhiteSpace(videoOut))
        {
            _logger.LogError("SubtitleStripper: Output has no video stream: {Temp}", temp);
            if (File.Exists(temp)) File.Delete(temp);
            return false;
        }

        // ── Step 4: Swap files ───────────────────────────────────────────────
        try
        {
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(inputPath, backup);
            File.Move(temp, inputPath);

            if (config.KeepBackup)
                _logger.LogInformation("SubtitleStripper: Done — backup at {Backup}", backup);
            else
            {
                File.Delete(backup);
                _logger.LogInformation("SubtitleStripper: Done — backup removed for {Path}", inputPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubtitleStripper: Failed to replace {Path}", inputPath);
            if (!File.Exists(inputPath) && File.Exists(backup))
            {
                try { File.Move(backup, inputPath); } catch { /* best effort */ }
            }
            if (File.Exists(temp)) File.Delete(temp);
            return false;
        }
    }
}
