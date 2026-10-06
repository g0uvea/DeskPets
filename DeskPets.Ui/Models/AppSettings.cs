using System.Collections.Generic;

namespace DeskPets.Ui.Models;

public class AppSettings
{
    public int Fps { get; set; } = 60;
    public int PetScale { get; set; } = 100;
    public string Language { get; set; } = "Português";
    public bool IsAlwaysOnTop { get; set; } = false;
    public List<PetAssetModel> ImportedPets { get; set; } = new();
}
