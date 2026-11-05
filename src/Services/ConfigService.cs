using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Source-generated JSON metadata for the config. Using a <see cref="JsonSerializerContext"/>
/// (instead of reflection-based serialization) keeps the app Native-AOT / trim safe.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(SharedConfig))]
internal partial class FidoJsonContext : JsonSerializerContext;

/// <summary>
/// Loads/saves <see cref="AppConfig"/> as JSON under %APPDATA%/Fido (migrating a legacy folder), and — when
/// <see cref="AppConfig.SharedDirectory"/> is set — the portable part (<see cref="SharedConfig"/>) as
/// <c>config.json</c> in that folder too.
///
/// The local file always holds everything, so it doubles as the last-known copy of the shared settings for
/// when the shared folder can't be read. On load the shared file, if readable, wins for its part. On save the
/// shared file is written only when its part has changed since it was last read or written here: a recent
/// branch saved on one machine must not put back the tools another machine has since changed.
/// </summary>
public sealed class ConfigService
{
    /// <summary>The file name used in the shared folder as well as the local one.</summary>
    public const string FileName = "config.json";

    public string ConfigDirectory { get; }
    public string ConfigFilePath { get; }

    // Legacy location from before the rename to Fido; read once so saved settings survive.
    // Null when an explicit directory is supplied (tests) so nothing leaks in from the real profile.
    private readonly string? _legacyConfigFilePath;

    /// <param name="configDirectory">
    /// Overrides where config.json lives; defaults to <c>%APPDATA%/Fido</c>. Supplying a directory
    /// (e.g. a temp folder in tests) also disables the legacy-location migration for isolation.
    /// </param>
    public ConfigService(string? configDirectory = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        ConfigDirectory = configDirectory ?? Path.Combine(appData, "Fido");
        ConfigFilePath = Path.Combine(ConfigDirectory, FileName);
        _legacyConfigFilePath = configDirectory is null
            ? Path.Combine(appData, "atlantic-opener", FileName)
            : null;
    }

    // The shared file this instance last read or wrote, and its shared part as serialized then — what a save
    // compares against to decide whether the shared file needs writing at all.
    private string? _sharedPath;
    private string? _sharedJson;

    /// <summary>
    /// <paramref name="sharedDirectory"/> as a full path — environment variables and a leading <c>~</c>
    /// expanded — or null when it's blank or can't be made into a path.
    /// </summary>
    public static string? ResolveSharedDirectory(string? sharedDirectory)
    {
        if (string.IsNullOrWhiteSpace(sharedDirectory)) return null;
        var path = Environment.ExpandEnvironmentVariables(sharedDirectory.Trim());
        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\'));
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The shared <c>config.json</c> <paramref name="sharedDirectory"/> names, or null for none.</summary>
    public static string? SharedFilePathFor(string? sharedDirectory) =>
        ResolveSharedDirectory(sharedDirectory) is { } dir ? Path.Combine(dir, FileName) : null;

    /// <summary>Reads the saved config, or returns sensible defaults if absent/corrupt.</summary>
    public AppConfig Load()
    {
        // Prefer the current location, then the legacy folder; a later Save migrates it forward.
        foreach (var path in new[] { ConfigFilePath, _legacyConfigFilePath })
        {
            if (path is null) continue;
            try
            {
                if (File.Exists(path))
                {
                    var cfg = JsonSerializer.Deserialize(File.ReadAllText(path), FidoJsonContext.Default.AppConfig);
                    if (cfg is not null)
                    {
                        ReadShared(cfg);
                        return Normalize(cfg);
                    }
                }
            }
            catch
            {
                // Corrupt or unreadable -> try the next location / fall back to defaults.
            }
        }
        return AppConfig.CreateDefault();
    }

    /// <summary>
    /// Writes the local file, then the shared one if there is one and its part has changed. When
    /// <see cref="AppConfig.SharedDirectory"/> now names a different folder from the one last read, and that
    /// folder already has a <c>config.json</c>, its settings are taken into <paramref name="config"/> first
    /// rather than overwritten — pointing a second machine at the folder joins it instead of replacing it.
    /// The local file is written before the shared one, so a shared folder that can't be written (a drive
    /// not mounted) still leaves this machine's settings saved; the failure then propagates.
    /// </summary>
    public void Save(AppConfig config)
    {
        var sharedPath = SharedFilePathFor(config.SharedDirectory);
        if (sharedPath is not null && !PathsEqual(sharedPath, _sharedPath))
            ReadShared(config);

        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(config, FidoJsonContext.Default.AppConfig));

        if (sharedPath is null)
        {
            _sharedPath = _sharedJson = null;
            return;
        }
        var json = JsonSerializer.Serialize(SharedConfig.From(config), FidoJsonContext.Default.SharedConfig);
        if (json == _sharedJson && PathsEqual(sharedPath, _sharedPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(sharedPath)!);
        File.WriteAllText(sharedPath, json);
        _sharedPath = sharedPath;
        _sharedJson = json;
    }

    /// <summary>
    /// Puts the shared file's settings over <paramref name="config"/>'s when it names one that can be read,
    /// and remembers what was read. A missing file leaves the config as it is and remembers nothing, so the
    /// next save writes it; an unreadable or corrupt one leaves it as it is too, but is remembered as read, so
    /// a save that changes nothing shared doesn't replace a file someone may be part-way through editing.
    /// </summary>
    private void ReadShared(AppConfig config)
    {
        var path = SharedFilePathFor(config.SharedDirectory);
        _sharedPath = path;
        _sharedJson = null;
        if (path is null) return;
        try
        {
            if (!File.Exists(path)) return;
            var shared = JsonSerializer.Deserialize(File.ReadAllText(path), FidoJsonContext.Default.SharedConfig);
            if (shared is null) return;
            shared.ApplyTo(config);
            Normalize(config);
            _sharedJson = JsonSerializer.Serialize(SharedConfig.From(config), FidoJsonContext.Default.SharedConfig);
        }
        catch
        {
            // Unreadable or corrupt: keep the local copy of the shared settings, and leave the file alone
            // until a shared setting actually changes.
            _sharedJson = JsonSerializer.Serialize(SharedConfig.From(config), FidoJsonContext.Default.SharedConfig);
        }
    }

    private static bool PathsEqual(string? a, string? b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static AppConfig Normalize(AppConfig cfg)
    {
        if (cfg.MainBranchNames is not { Count: > 0 })
            cfg.MainBranchNames = new() { "main", "master" };
        if (cfg.SearchDepth <= 0)
            cfg.SearchDepth = 4;
        cfg.CloseAfterOpenDelaySeconds = Math.Clamp(cfg.CloseAfterOpenDelaySeconds, 0, AppConfig.MaxCloseAfterOpenDelaySeconds);
        cfg.SearchRoots ??= new();
        cfg.RecentBranches ??= new();
        cfg.RecentSolutions ??= new();
        cfg.NewBranchRepos ??= new();
        cfg.Shortcuts ??= new();

        // Seed the editor list for configs written before multi-editor support, carrying a legacy
        // explicit Rider path forward onto the Rider editor so the old setting isn't lost.
        cfg.Editors ??= new();
        if (cfg.Editors.Count == 0)
        {
            cfg.Editors = Editor.Defaults();
            if (!string.IsNullOrWhiteSpace(cfg.RiderPath))
            {
                var rider = cfg.Editors.FirstOrDefault(e => e.Kind == EditorKind.Rider);
                if (rider is not null) rider.Path = cfg.RiderPath;
            }
        }
        // One-time, version-gated migrations carrying older configs forward as built-ins are added.
        if (cfg.ConfigVersion < 1)
        {
            // WebStorm became a built-in editor. Append it to lists that predate it so it shows up after an
            // upgrade — appended rather than inserted, so existing editor positions (and the default-editor
            // index, which is a position) are preserved. The version stamp below stops this re-running, so a
            // user who then removes WebStorm keeps it removed.
            if (cfg.Editors.All(e => e.Kind != EditorKind.WebStorm))
                cfg.Editors.Add(Editor.Defaults().First(e => e.Kind == EditorKind.WebStorm));
        }
        if (cfg.ConfigVersion < 2)
        {
            // Console and File Explorer became built-in open targets. Append whichever a pre-v2 config is
            // missing — appended, like WebStorm, so existing editor positions and the default index hold.
            // The version stamp below stops this re-running, so removing them later makes the removal stick.
            foreach (var kind in new[] { EditorKind.Console, EditorKind.FileExplorer })
                if (cfg.Editors.All(e => e.Kind != kind))
                    cfg.Editors.Add(Editor.Defaults().First(e => e.Kind == kind));
        }
        cfg.ConfigVersion = AppConfig.CurrentConfigVersion;

        // NoDefaultEditor (-1) is a deliberate "equal-weight grid, no hero" choice — preserve it;
        // anything else clamps back into the editor list.
        if (cfg.DefaultEditorIndex != AppConfig.NoDefaultEditor)
            cfg.DefaultEditorIndex = Math.Clamp(cfg.DefaultEditorIndex, 0, cfg.Editors.Count - 1);
        return cfg;
    }
}
