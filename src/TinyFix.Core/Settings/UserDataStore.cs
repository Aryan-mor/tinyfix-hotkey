// Loads and saves settings.json and skills.json, and imports the Python app's files once.

using System.Globalization;
using System.Text.Json;
using TinyFix.Core.IO;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;
using TinyFix.Core.Skills;

namespace TinyFix.Core.Settings;

/// <summary>The user's settings and skills on disk.</summary>
public sealed class UserDataStore
{
    /// <summary>Settings file name.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>Environment variable that names the folder of the Python app to import from (tests, portable installs).</summary>
    public const string LegacyVariable = "TINYFIX_LEGACY_DIR";

    /// <summary>Creates a store over <paramref name="directory"/> (nothing is read until <see cref="Load"/>).</summary>
    public UserDataStore(string directory) => Directory = directory;

    /// <summary>The data folder.</summary>
    public string Directory { get; }

    /// <summary>Where the Python app's files are looked for; tests replace it so they never touch a real installation.</summary>
    public Func<string?> LegacySource { get; init; } = LegacyDirectory;

    /// <summary>Current settings.</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>Current skills.</summary>
    public SkillStore Skills { get; private set; } = SkillCatalog.DefaultStore();

    /// <summary>Raised after settings or skills were saved.</summary>
    public event EventHandler? Changed;

    /// <summary>Full path of settings.json.</summary>
    public string SettingsPath => Path.Combine(Directory, SettingsFileName);

    /// <summary>Full path of skills.json.</summary>
    public string SkillsPath => Path.Combine(Directory, SkillStoreFile.FileName);

    /// <summary>Reads both files (defaults when missing or damaged) and imports the Python app's files the first time.</summary>
    public void Load()
    {
        Settings = ReadSettings(SettingsPath);
        if (!Settings.LegacyImported)
        {
            ImportLegacy(LegacySource());
            Settings.LegacyImported = true;
            SaveSettings();
        }
        var (store, created) = SkillStoreFile.Load(SkillsPath);
        Skills = store;
        if (created)
            SaveSkills();
    }

    /// <summary>The folder of an installed Python app, if this OS and environment have one.</summary>
    public static string? LegacyDirectory()
    {
        var variable = Environment.GetEnvironmentVariable(LegacyVariable);
        if (!string.IsNullOrWhiteSpace(variable))
            return variable;
        // A data-dir override means a test or portable copy: never reach into the real installation.
        if (DataDirectory.Override is not null || !OperatingSystem.IsWindows())
            return null;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TinyFix Hotkey");
    }

    /// <summary>Copies skills.json, secrets.dpapi and the online settings of config.ini from <paramref name="legacy"/> when ours are absent.</summary>
    internal void ImportLegacy(string? legacy)
    {
        if (legacy is null || !System.IO.Directory.Exists(legacy))
            return;
        try
        {
            var oldSkills = Path.Combine(legacy, SkillStoreFile.FileName);
            if (!File.Exists(SkillsPath) && File.Exists(oldSkills))
            {
                var (store, _) = SkillStoreFile.Load(oldSkills);
                SkillStoreFile.Save(SkillsPath, store);
            }
            var oldSecrets = Path.Combine(legacy, "secrets.dpapi");
            var newSecrets = Path.Combine(Directory, "secrets.dpapi");
            if (!File.Exists(newSecrets) && File.Exists(oldSecrets))
                AtomicFile.WriteAllBytes(newSecrets, File.ReadAllBytes(oldSecrets));
            var config = Path.Combine(legacy, "config.ini");
            if (File.Exists(config))
            {
                ApplyConfigIni(File.ReadAllLines(config), Settings);
                FinishLegacyEndpoints(Settings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken old installation must never stop the new app from starting.
        }
    }

    /// <summary>Reads the <c>[TinyFix]</c> keys of the Python app's config.ini that still mean something here.</summary>
    internal static void ApplyConfigIni(IEnumerable<string> lines, AppSettings settings)
    {
        var inSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals("[TinyFix]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (!inSection || eq <= 0)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "llm_provider" when value.Length > 0:
                    settings.Llm.Main.Protocol = value;
                    break;
                case "llm_model" when value.Length > 0:
                    settings.Llm.Main.Model = value;
                    break;
                case "llm_base_url":
                    settings.Llm.Main.BaseUrl = value;
                    break;
                case "llm_fallback_model" when value.Length > 0:
                    settings.Llm.Fallback ??= new LlmEndpointSettings();
                    settings.Llm.Fallback.Model = value;
                    break;
                case "llm_fallback_provider":
                    settings.Llm.Fallback ??= new LlmEndpointSettings();
                    settings.Llm.Fallback.Protocol = value;
                    break;
                case "llm_target_language" when value.Length > 0:
                    settings.Variables[VariableNames.Language] = value;
                    break;
                case "llm_enabled":
                    // Older files had an on/off switch. A configured model is used; the key is ignored.
                    break;
                case "llm_timeout" when double.TryParse(value, CultureInfo.InvariantCulture, out var t) && t > 0:
                    settings.Llm.Main.Timeout = t;
                    if (settings.Llm.Fallback is not null)
                        settings.Llm.Fallback.Timeout = t;
                    break;
            }
        }
    }

    private static AppSettings ReadSettings(string path)
    {
        var settings = new AppSettings();
        string? json = null;
        try
        {
            if (File.Exists(path))
            {
                json = File.ReadAllText(path);
                settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged settings file means defaults, not a crash.
            settings = new AppSettings();
            json = null;
        }
        if (json is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                SettingsMigration.Apply(settings, doc.RootElement);
            }
            catch (JsonException)
            {
                // The serializer already accepted the file; a second parse failure just skips legacy keys.
            }
        }
        settings.Normalize();
        return settings;
    }

    /// <summary>Turns provider ids copied out of config.ini into presets, using the base URL when it tells them apart.</summary>
    private static void FinishLegacyEndpoints(AppSettings settings)
    {
        var main = settings.Llm.Main;
        main.Protocol = LlmProtocols.MapLegacy(main.Protocol, main.BaseUrl);
        if (settings.Llm.Fallback is { } fallback)
            fallback.Protocol = LlmProtocols.MapLegacy(string.IsNullOrWhiteSpace(fallback.Protocol) ? main.Protocol : fallback.Protocol, fallback.BaseUrl);
        settings.Normalize();
    }

    /// <summary>Writes settings.json atomically and raises <see cref="Changed"/>.</summary>
    public void SaveSettings()
    {
        AtomicFile.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, SettingsJsonContext.Default.AppSettings) + "\n");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes skills.json atomically and raises <see cref="Changed"/>.</summary>
    public void SaveSkills()
    {
        SkillStoreFile.Save(SkillsPath, Skills);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
