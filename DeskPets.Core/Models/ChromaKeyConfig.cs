namespace DeskPets.Core.Models;

public class ChromaKeyConfig
{
    public bool IsEnabled { get; set; } = false;
    public string KeyColorHex { get; set; } = "#FF0000";
    public double Similarity { get; set; } = 35.0; // Tolerância em porcentagem (0 - 100)
    public double Smoothness { get; set; } = 15.0; // Suavização de bordas em porcentagem (0 - 50)
}
