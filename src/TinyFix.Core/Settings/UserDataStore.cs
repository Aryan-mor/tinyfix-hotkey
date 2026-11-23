// Loads and saves settings.json and skills.json, and imports the Python app's files once.

using System.Text.Json;
using TinyFix.Core.IO;
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
                ApplyConfigIni(File.ReadAllLines(config), Settings);
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
                case "llm_provider" when value.Length > 0: settings.LlmProvider = value; break;
                case "llm_model" when value.Length > 0: settings.LlmModel = value; break;
                case "llm_base_url": settings.LlmBaseUrl = value; break;
                case "llm_fallback_model" when value.Length > 0: settings.LlmFallbackModel = value; break;
                case "llm_fallback_provider": settings.LlmFallbackProvider = value; break;
                case "llm_target_language" when value.Length > 0: settings.LlmTargetLanguage = value; break;
                case "llm_enabled": settings.LlmEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "llm_timeout" when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var t) && t > 0:
                    settings.LlmTimeout = t;
                    break;
            }
        }
    }

    private static AppSettings ReadSettings(string path)
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged settings file means defaults, not a crash.
            settings = new AppSettings();
        }
        settings.Donut ??= new DonutSettings();
        settings.Donut.Normalize();
        return settings;
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
