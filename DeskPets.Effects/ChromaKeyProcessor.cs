using System;
using System.IO;
using SkiaSharp;

namespace DeskPets.Effects;

public static class ChromaKeyProcessor
{
    private const double MaxRgbDistance = 441.67295593; // sqrt(255^2 * 3)

    /// <summary>
    /// Converte string hexadecimal (#RRGGBB ou #AARRGGBB) para SKColor com segurança.
    /// </summary>
    public static SKColor ParseColor(string hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new SKColor(r, g, b, 255);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return new SKColor(r, g, b, a);
            }
        }
        catch { }
        return fallback;
    }

    /// <summary>
    /// Amostra a cor exata de um pixel da imagem nas coordenadas (x, y).
    /// </summary>
    public static SKColor SampleColor(SKBitmap bitmap, int x, int y)
    {
        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return SKColors.Transparent;

        int clampedX = Math.Clamp(x, 0, bitmap.Width - 1);
        int clampedY = Math.Clamp(y, 0, bitmap.Height - 1);
        return bitmap.GetPixel(clampedX, clampedY);
    }

    /// <summary>
    /// Aplica o algoritmo de Chroma Key de alta performance diretamente nos pixels de um SKBitmap.
    /// Retorna uma nova instância de SKBitmap com transparência aplicada.
    /// </summary>
    public static unsafe SKBitmap ApplyChromaKey(SKBitmap source, SKColor keyColor, double tolerance, double smoothness)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var result = new SKBitmap(info);

        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);
        }

        int keyR = keyColor.Red;
        int keyG = keyColor.Green;
        int keyB = keyColor.Blue;

        double tol = Math.Clamp(tolerance, 1.0, 100.0);
        double sm = Math.Clamp(smoothness, 0.0, 50.0);

        double lowerBound = Math.Max(0.0, tol - sm);
        double upperBound = Math.Min(100.0, tol + sm);
        double range = Math.Max(0.001, upperBound - lowerBound);

        int totalPixels = source.Width * source.Height;
        byte* ptr = (byte*)result.GetPixels().ToPointer();

        double lowerBoundSq = (lowerBound / 100.0) * (lowerBound / 100.0) * 195075.0;
        double upperBoundSq = (upperBound / 100.0) * (upperBound / 100.0) * 195075.0;

        // Bgra8888: [0] = Blue, [1] = Green, [2] = Red, [3] = Alpha
        for (int i = 0; i < totalPixels; i++, ptr += 4)
        {
            byte a = ptr[3];
            if (a == 0) continue;

            byte b = ptr[0];
            byte g = ptr[1];
            byte r = ptr[2];

            int dR = r - keyR;
            int dG = g - keyG;
            int dB = b - keyB;
            int distSq = dR * dR + dG * dG + dB * dB;

            if (distSq <= lowerBoundSq)
            {
                ptr[3] = 0;
            }
            else if (distSq < upperBoundSq)
            {
                double dist = Math.Sqrt(distSq);
                double distPercent = (dist / MaxRgbDistance) * 100.0;
                double factor = (distPercent - lowerBound) / range;
                factor = factor * factor * (3.0 - 2.0 * factor); // smoothstep
                ptr[3] = (byte)Math.Clamp((int)(a * factor), 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// Aplica Chroma Key em bytes codificados de imagem (PNG/JPG) e retorna os novos bytes PNG com transparência.
    /// </summary>
    public static byte[]? ApplyChromaKeyToPngBytes(byte[] imageBytes, string keyColorHex, double tolerance, double smoothness)
    {
        if (imageBytes == null || imageBytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(imageBytes);
            using var originalBitmap = SKBitmap.Decode(stream);
            if (originalBitmap == null) return null;

            var keyColor = ParseColor(keyColorHex, SKColors.Green);
            using var keyedBitmap = ApplyChromaKey(originalBitmap, keyColor, tolerance, smoothness);

            using var image = SKImage.FromBitmap(keyedBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 75);
            return data.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChromaKeyProcessor] Erro ao aplicar chroma key: {ex.Message}");
            return null;
        }
    }
}
