using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeskPets.Core.Models;

namespace DeskPets.Core.Services;

public static class PetStorageService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskPets");

    private static readonly string PetsFilePath = Path.Combine(ConfigDirectory, "pets.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static List<PetAssetModel> LoadPets()
    {
        try
        {
            if (File.Exists(PetsFilePath))
            {
                var json = File.ReadAllText(PetsFilePath);
                var pets = JsonSerializer.Deserialize<List<PetAssetModel>>(json, JsonOptions);
                if (pets != null)
                {
                    return pets;
                }
            }
            else
            {
                // Migração transparente de versão anterior onde os pets ficavam em settings.json
                string legacySettingsPath = Path.Combine(ConfigDirectory, "settings.json");
                if (File.Exists(legacySettingsPath))
                {
                    var settingsJson = File.ReadAllText(legacySettingsPath);
                    using var doc = JsonDocument.Parse(settingsJson);
                    if (doc.RootElement.TryGetProperty("ImportedPets", out var petsProp) &&
                        petsProp.ValueKind == JsonValueKind.Array)
                    {
                        var migratedPets = JsonSerializer.Deserialize<List<PetAssetModel>>(petsProp.GetRawText(), JsonOptions);
                        if (migratedPets != null && migratedPets.Count > 0)
                        {
                            SavePets(migratedPets);
                            return migratedPets;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PetStorageService] Erro ao carregar pets: {ex.Message}");
        }

        return new List<PetAssetModel>();
    }

    public static void SavePets(IEnumerable<PetAssetModel> pets)
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            var json = JsonSerializer.Serialize(pets, JsonOptions);
            File.WriteAllText(PetsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PetStorageService] Erro ao salvar pets: {ex.Message}");
        }
    }
}
