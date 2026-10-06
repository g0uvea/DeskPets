using System;
using System.IO;
using System.Text.Json;
using DeskPets.Ui.Models;

namespace DeskPets.Ui.Services;

public static class SettingsService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskPets");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao carregar configurações: {ex.Message}");
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao salvar configurações: {ex.Message}");
        }
    }
}
