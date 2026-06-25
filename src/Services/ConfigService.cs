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
internal partial class FidoJsonContext : JsonSerializerContext;

/// <summary>Loads/saves <see cref="AppConfig"/> as JSON under %APPDATA%/Fido (migrating a legacy folder).</summary>
public sealed class ConfigService
{
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
        ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");
        _legacyConfigFilePath = configDirectory is null
            ? Path.Combine(appData, "atlantic-opener", "config.json")
            : null;
    }

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
                    if (cfg is not null) return Normalize(cfg);
                }
            }
            catch
            {
                // Corrupt or unreadable -> try the next location / fall back to defaults.
            }
        }
        return AppConfig.CreateDefault();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(config, FidoJsonContext.Default.AppConfig));
    }

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
        cfg.DefaultEditorIndex = Math.Clamp(cfg.DefaultEditorIndex, 0, cfg.Editors.Count - 1);
        return cfg;
    }
}
