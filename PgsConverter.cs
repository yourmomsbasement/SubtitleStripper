using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleStripper;

/// <summary>
/// Calls pgsrip directly on the video file to OCR PGS subtitle streams to .srt sidecars.
/// pgsrip handles extraction, OCR, and output internally — no temp files needed.
///
/// Language codes must be IETF format (e.g. "en", "pt-BR"), not ISO 639-2.
/// </summary>
public sealed class PgsConverter
{
    private readonly ILogger _logger;
    private readonly string _ffprobePath;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public PgsConverter(ILogger logger, string ffprobePath)
    {
        _logger      = logger;
        _ffprobePath = ffprobePath;
    }

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs pgsrip on <paramref name="videoPath"/>, converting any PGS streams
    /// to .srt sidecars beside the video.  Returns <c>true</c> if pgsrip ran
    /// successfully (or there was nothing to do).
    /// </summary>
    public async Task<bool> ConvertAsync(string videoPath, CancellationToken ct)
    {
        // Quick check — skip files with no PGS streams so we don't invoke pgsrip unnecessarily.
        if (!await HasPgsStreamsAsync(videoPath, ct).ConfigureAwait(false))
        {
            _logger.LogDebug("PgsConverter: No PGS streams in {Path}", videoPath);
            return true;
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var exe    = string.IsNullOrWhiteSpace(config.PgsRipPath) ? "pgsrip" : config.PgsRipPath;

        _logger.LogInformation("PgsConverter: Running pgsrip on {Path}", videoPath);

        var psi = BuildPsi(exe);

        // Language filter — pgsrip accepts IETF codes via repeated -l flags.
        foreach (var lang in GetLanguageFilter())
        {
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add(lang);
        }

        psi.ArgumentList.Add(videoPath);

        using var process = new Process { StartInfo = psi };
        try { process.Start(); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PgsConverter: Failed to start pgsrip (path: {Exe}). Is it installed?", exe);
            return false;
        }

        process.StandardInput.Close();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogError("PgsConverter: pgsrip failed for {Path}: {Err}", videoPath, stderr);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(stdout))
            _logger.LogInformation("PgsConverter: {Output}", stdout.Trim());

        return true;
    }

    // -------------------------------------------------------------------------
    // PGS detection
    // -------------------------------------------------------------------------

    private async Task<bool> HasPgsStreamsAsync(string videoPath, CancellationToken ct)
    {
        var psi = BuildPsi(_ffprobePath);
        foreach (var a in new[]
        {
            "-v", "error",
            "-select_streams", "s",
            "-show_entries", "stream=codec_name",
            "-of", "json",
            "--", videoPath
        })
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();

        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        try
        {
            var doc = JsonSerializer.Deserialize<FfprobeStreamsDoc>(stdout, JsonOpts);
            if (doc?.Streams is null) return false;

            foreach (var s in doc.Streams)
            {
                var codec = s.CodecName ?? string.Empty;
                if (codec.Equals("hdmv_pgs_subtitle", StringComparison.OrdinalIgnoreCase) ||
                    codec.Equals("pgssub",             StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PgsConverter: Failed to parse ffprobe output for {Path}", videoPath);
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    private static ProcessStartInfo BuildPsi(string exe) => new()
    {
        FileName               = exe,
        UseShellExecute        = false,
        CreateNoWindow         = true,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        RedirectStandardInput  = true
    };

    /// <summary>
    /// Returns language codes from config as a list, or empty meaning "all languages".
    /// Codes should be IETF format (e.g. "en", "pt-BR").
    /// </summary>
    private static List<string> GetLanguageFilter()
    {
        var raw  = Plugin.Instance?.Configuration.PgsLanguageFilter ?? string.Empty;
        var list = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrEmpty(part))
                list.Add(part);
        }
        return list;
    }

    // -------------------------------------------------------------------------
    // JSON model for ffprobe output
    // -------------------------------------------------------------------------

    private sealed record FfprobeStreamsDoc(
        [property: JsonPropertyName("streams")] List<FfprobeStream>? Streams);

    private sealed record FfprobeStream(
        [property: JsonPropertyName("codec_name")] string? CodecName);
}
