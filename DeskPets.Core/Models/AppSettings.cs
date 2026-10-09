namespace DeskPets.Core.Models;

public class AppSettings
{
    public int Fps { get; set; } = 60;
    public int PetScale { get; set; } = 100;
    public int PetBaseWidth { get; set; } = 600;
    public int PetBaseHeight { get; set; } = 600;
    public string Language { get; set; } = "Português";
    public bool IsAlwaysOnTop { get; set; } = false;
    public bool EnableAnimations { get; set; } = true;
    public bool AllowDuplicatePets { get; set; } = true;
}

