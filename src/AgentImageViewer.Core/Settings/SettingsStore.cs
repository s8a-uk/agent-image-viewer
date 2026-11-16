using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentImageViewer.Core.Settings;

/// <summary>Reads and writes <see cref="AppSettings"/> as a JSON file. Neither direction ever throws for I/O problems.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public SettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Returns the stored settings with out-of-range values clamped. A value that cannot be read (say, a theme name
    /// from a newer version) keeps its default without affecting the others; an unreadable file gives the defaults.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath) && JsonNode.Parse(File.ReadAllText(_filePath)) is JsonObject stored)
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(ReadableProperties(stored), JsonOptions) ?? new AppSettings();
                settings.MaxCards = AppSettings.ClampMaxCards(settings.MaxCards);
                settings.Opacity = AppSettings.ClampOpacity(settings.Opacity);
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall back to the defaults; the file is rewritten on the next save.
        }

        return new AppSettings();
    }

    /// <summary>Writes the file in one step: a crash or full disk midway leaves the previous file intact.</summary>
    public void Save(AppSettings settings)
    {
        var temporary = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; losing a save must not take the viewer down.
            TryDelete(temporary);
        }
    }

    // Keeps only the properties that deserialize on their own.
    private static JsonObject ReadableProperties(JsonObject stored)
    {
        var readable = new JsonObject();
        foreach (var (name, value) in stored)
        {
            var single = new JsonObject { [name] = value?.DeepClone() };
            try
            {
                _ = single.Deserialize<AppSettings>(JsonOptions);
                readable[name] = value?.DeepClone();
            }
            catch (JsonException)
            {
                // Dropped: the property keeps its default.
            }
        }

        return readable;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stray temporary file is harmless; the next save overwrites it.
        }
    }
}
