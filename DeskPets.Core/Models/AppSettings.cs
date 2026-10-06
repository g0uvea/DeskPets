namespace DeskPets.Core.Models;

public class AppSettings
{
    public int Fps { get; set; } = 60;
    public int PetScale { get; set; } = 100;
    public string Language { get; set; } = "Português";
    public bool IsAlwaysOnTop { get; set; } = false;
    public bool EnableAnimations { get; set; } = true;
}

