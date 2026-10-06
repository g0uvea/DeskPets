using System;

namespace DeskPets.Core.Media;

public class MediaFrame
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(33); // ~30 fps default
}
