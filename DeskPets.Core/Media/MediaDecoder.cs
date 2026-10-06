using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace DeskPets.Core.Media;

public static class MediaDecoder
{
    private static readonly HashSet<string> StaticImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".ico"
    };

    private static readonly HashSet<string> AnimatedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gif", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".mkv", ".avi", ".av1"
    };

    public static string DetectMediaType(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        if (VideoExtensions.Contains(ext)) return "Video";
        if (AnimatedImageExtensions.Contains(ext)) return "Animation";
        return "Image";
    }

    public static bool IsVideo(string filePath) => VideoExtensions.Contains(Path.GetExtension(filePath));
    public static bool IsAnimated(string filePath) => AnimatedImageExtensions.Contains(Path.GetExtension(filePath));

    /// <summary>
    /// Extrai um thumbnail em formato PNG (bytes) para qualquer mídia suportada (mp4, webp, webm, png, jpg, gif, mov, av1).
    /// </summary>
    public static async Task<byte[]?> ExtractThumbnailBytesAsync(string filePath, int maxWidth = 250, int maxHeight = 250)
    {
        if (!File.Exists(filePath)) return null;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Se for vídeo (mp4, webm, mov, av1...), usamos ffmpeg para extrair o primeiro frame
        if (VideoExtensions.Contains(ext))
        {
            return await ExtractVideoFrameAsync(filePath, 0.0, maxWidth, maxHeight);
        }

        // Se for imagem estática ou animada (png, jpg, webp, gif...), usamos SkiaSharp
        return await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var codec = SKCodec.Create(stream);
                if (codec != null)
                {
                    var info = codec.Info;
                    // Calcula escala mantendo proporção
                    float scale = Math.Min((float)maxWidth / info.Width, (float)maxHeight / info.Height);
                    if (scale > 1.0f) scale = 1.0f;

                    int targetW = Math.Max(1, (int)(info.Width * scale));
                    int targetH = Math.Max(1, (int)(info.Height * scale));

                    var imageInfo = new SKImageInfo(targetW, targetH, info.ColorType, info.AlphaType);
                    using var bitmap = new SKBitmap(imageInfo);

                    var options = new SKCodecOptions(0); // primeiro frame para GIFs/WebP
                    var result = codec.GetPixels(imageInfo, bitmap.GetPixels(), options);

                    if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
                    {
                        using var image = SKImage.FromBitmap(bitmap);
                        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                        return data.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaDecoder] Erro ao decodificar imagem {filePath}: {ex.Message}");
            }

            // Fallback: tentar ler os bytes diretamente se for imagem estática
            try
            {
                if (StaticImageExtensions.Contains(ext))
                {
                    return File.ReadAllBytes(filePath);
                }
            }
            catch { }

            return null;
        });
    }

    /// <summary>
    /// Extrai múltiplos quadros de imagens animadas (GIF, WebP) ou vídeos respeitando o FPS informado.
    /// </summary>
    public static async Task<List<MediaFrame>> ExtractFramesAsync(string filePath, int targetFps = 30, int maxDurationSeconds = 5)
    {
        var frames = new List<MediaFrame>();
        if (!File.Exists(filePath)) return frames;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Para imagens animadas usando SkiaSharp SKCodec
        if (AnimatedImageExtensions.Contains(ext))
        {
            return await Task.Run(() =>
            {
                var list = new List<MediaFrame>();
                try
                {
                    using var stream = File.OpenRead(filePath);
                    using var codec = SKCodec.Create(stream);
                    if (codec != null)
                    {
                        int frameCount = codec.FrameCount;
                        var frameInfos = codec.FrameInfo;
                        var info = codec.Info;

                        for (int i = 0; i < frameCount; i++)
                        {
                            using var bitmap = new SKBitmap(info);
                            var options = new SKCodecOptions(i);
                            if (codec.GetPixels(info, bitmap.GetPixels(), options) == SKCodecResult.Success)
                            {
                                using var image = SKImage.FromBitmap(bitmap);
                                using var data = image.Encode(SKEncodedImageFormat.Png, 85);

                                int durationMs = frameInfos.Length > i ? frameInfos[i].Duration : 33;
                                if (durationMs <= 0) durationMs = 33;

                                list.Add(new MediaFrame
                                {
                                    Data = data.ToArray(),
                                    Width = info.Width,
                                    Height = info.Height,
                                    Duration = TimeSpan.FromMilliseconds(durationMs)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MediaDecoder] Erro ao extrair frames de {filePath}: {ex.Message}");
                }
                return list;
            });
        }

        // Para vídeos (mp4, webm, mov, av1), usa FFmpeg para extrair frames no FPS alvo
        if (VideoExtensions.Contains(ext))
        {
            return await ExtractVideoFramesViaFfmpegAsync(filePath, targetFps, maxDurationSeconds);
        }

        // Imagem estática simples: 1 frame
        var singleThumb = await ExtractThumbnailBytesAsync(filePath);
        if (singleThumb != null)
        {
            frames.Add(new MediaFrame
            {
                Data = singleThumb,
                Duration = TimeSpan.FromSeconds(1)
            });
        }

        return frames;
    }

    private static async Task<byte[]?> ExtractVideoFrameAsync(string filePath, double timeSeconds, int maxWidth, int maxHeight)
    {
        return await Task.Run(() =>
        {
            try
            {
                string timeStr = timeSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-loglevel error -ss {timeStr} -i \"{filePath}\" -vframes 1 -vf \"scale={maxWidth}:{maxHeight}:force_original_aspect_ratio=decrease\" -f image2pipe -vcodec png -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                using var ms = new MemoryStream();
                process.StandardOutput.BaseStream.CopyTo(ms);
                process.WaitForExit(3000);

                if (ms.Length > 0)
                {
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaDecoder] FFmpeg não disponível ou erro: {ex.Message}");
            }
            return null;
        });
    }

    private static async Task<List<MediaFrame>> ExtractVideoFramesViaFfmpegAsync(string filePath, int fps, int maxDurationSeconds)
    {
        return await Task.Run(() =>
        {
            var frames = new List<MediaFrame>();
            string tempDir = Path.Combine(Path.GetTempPath(), "deskpets_frames_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string outputPattern = Path.Combine(tempDir, "frame_%04d.png");

                int safeFps = Math.Clamp(fps, 1, 60);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-loglevel error -t {maxDurationSeconds} -i \"{filePath}\" -vf \"fps={safeFps},scale=200:200:force_original_aspect_ratio=decrease\" \"{outputPattern}\"",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                process.WaitForExit(5000);

                var generatedFiles = Directory.GetFiles(tempDir, "frame_*.png");
                Array.Sort(generatedFiles);

                var frameDuration = TimeSpan.FromSeconds(1.0 / safeFps);
                foreach (var file in generatedFiles)
                {
                    byte[] data = File.ReadAllBytes(file);
                    frames.Add(new MediaFrame
                    {
                        Data = data,
                        Duration = frameDuration
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaDecoder] Erro ao extrair frames de vídeo: {ex.Message}");
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
            return frames;
        });
    }
}
