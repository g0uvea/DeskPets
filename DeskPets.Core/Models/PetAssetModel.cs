using System;

namespace DeskPets.Core.Models;

public class PetAssetModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "Asset";
    public string? FilePath { get; set; }
    public string MediaType { get; set; } = "Image";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ChromaKeyConfig? ChromaKey { get; set; }
    public int? Scale { get; set; }
    public int? Fps { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool DesktopInteractionEnabled { get; set; }
    public string? DesktopInteractionType { get; set; }
}
