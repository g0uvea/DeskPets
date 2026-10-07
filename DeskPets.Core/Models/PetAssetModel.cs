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
}
