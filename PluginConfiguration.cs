using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitleStripper;

/// <summary>
/// Settings exposed in the Jellyfin Dashboard → Plugins → Subtitle Stripper.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ── General ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Override the ffmpeg binary path.
    /// Leave empty to use the path Jellyfin already knows about.
    /// </summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>Keep a .bak copy of the original file after stripping subtitles.</summary>
    public bool KeepBackup { get; set; } = true;

    /// <summary>Log what would happen without modifying any files.</summary>
    public bool DryRun { get; set; } = false;

    /// <summary>Automatically process files as soon as they are added to the library.</summary>
    public bool ProcessOnItemAdded { get; set; } = true;

    // ── PGS → SRT Conversion ─────────────────────────────────────────────────

    /// <summary>
    /// Before stripping subtitles, OCR any PGS (image-based Blu-ray) subtitle streams
    /// and save them as .srt sidecar files so the text is preserved.
    /// Requires pgsrip and Tesseract to be installed on the host.
    /// </summary>
    public bool ConvertPgsToSrt { get; set; } = false;

    /// <summary>
    /// Path to the pgsrip executable.
    /// Leave empty to search PATH (e.g. if installed via pip install pgsrip).
    /// </summary>
    public string PgsRipPath { get; set; } = string.Empty;

    /// <summary>
    /// ISO 639-2 language code passed to Tesseract when no language tag is found in
    /// the subtitle stream (e.g. "eng", "fra", "deu").
    /// </summary>
    public string FallbackOcrLanguage { get; set; } = "eng";

    /// <summary>
    /// Comma-separated list of ISO 639-2 language codes to convert (e.g. "eng,fra").
    /// Only PGS streams whose language tag matches one of these will be OCR'd.
    /// Leave empty to convert all languages.
    /// </summary>
    public string PgsLanguageFilter { get; set; } = string.Empty;

    // ── Library Scope ─────────────────────────────────────────────────────────

    /// <summary>
    /// Comma-separated list of library (collection folder) IDs to process.
    /// Leave empty to process all libraries.
    /// </summary>
    public string SelectedLibraryIds { get; set; } = string.Empty;
}
