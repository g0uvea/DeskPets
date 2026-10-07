using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace DeskPets.Core.Media;

public static class MediaDecoder
{
    private static readonly ConcurrentDictionary<string, List<MediaFrame>> _framesCache = new();

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
    /// Obtém a duração aproximada em segundos de uma mídia (vídeo ou animação).
    /// </summary>
    public static async Task<double> GetMediaDurationSecondsAsync(string filePath)
    {
        if (!File.Exists(filePath)) return 0;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // 1. Se for vídeo, obtém duração via ffprobe (ou fallback com ffmpeg)
        if (VideoExtensions.Contains(ext))
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = startInfo };
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(4000);

                    if (double.TryParse(output, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double duration))
                    {
                        return duration;
                    }
                }
                catch { }

                // Fallback: parsear saída de ffmpeg -i
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{filePath}\"",
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = startInfo };
                    process.Start();
                    string err = process.StandardError.ReadToEnd();
                    process.WaitForExit(4000);

                    int idx = err.IndexOf("Duration: ", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string sub = err.Substring(idx + 10, 11).Trim();
                        if (TimeSpan.TryParse(sub, out var parsedSpan))
                        {
                            return parsedSpan.TotalSeconds;
                        }
                    }
                }
                catch { }

                return 0.0;
            });
        }

        // 2. Se for animação (GIF, WebP), calcula pela soma das durações dos frames no SKCodec
        if (AnimatedImageExtensions.Contains(ext))
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(filePath);
                    using var codec = SKCodec.Create(stream);
                    if (codec != null && codec.FrameInfo != null)
                    {
                        long totalMs = 0;
                        foreach (var f in codec.FrameInfo)
                        {
                            totalMs += f.Duration > 0 ? f.Duration : 33;
                        }
                        return totalMs / 1000.0;
                    }
                }
                catch { }
                return 0.0;
            });
        }

        return 0.0;
    }

    /// <summary>
    /// Limpa todo o cache de quadros da memória.
    /// </summary>
    public static void ClearCache() => _framesCache.Clear();

    /// <summary>
    /// Invalida o cache de um arquivo específico.
    /// </summary>
    public static void InvalidateCache(string filePath)
    {
        foreach (var key in _framesCache.Keys)
        {
            if (key.StartsWith(filePath, StringComparison.OrdinalIgnoreCase))
            {
                _framesCache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Extrai múltiplos quadros de imagens animadas (GIF, WebP) ou vídeos respeitando o FPS informado (até 30 segundos).
    /// Utiliza cache em memória para que chamadas subsequentes respondam em 0ms.
    /// </summary>
    public static async Task<List<MediaFrame>> ExtractFramesAsync(string filePath, int targetFps = 60, int maxDurationSeconds = 30)
    {
        if (!File.Exists(filePath)) return new List<MediaFrame>();

        string cacheKey = $"{filePath}_{targetFps}_{maxDurationSeconds}_hd640";
        if (_framesCache.TryGetValue(cacheKey, out var cached) && cached.Count > 0)
        {
            return cached;
        }

        var frames = new List<MediaFrame>();
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Para imagens animadas usando SkiaSharp SKCodec
        if (AnimatedImageExtensions.Contains(ext))
        {
            frames = await Task.Run(() =>
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
        else if (VideoExtensions.Contains(ext))
        {
            // Para vídeos (mp4, webm, mov, av1), usa FFmpeg ultra-otimizado em qualidade HD e até 60 FPS
            frames = await ExtractVideoFramesViaFfmpegAsync(filePath, targetFps, maxDurationSeconds);
        }
        else
        {
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
        }

        if (frames.Count > 0)
        {
            _framesCache[cacheKey] = frames;
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

                // Permite até 60 FPS reais para máxima fluidez nos vídeos
                int safeFps = Math.Clamp(fps, 15, 60);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-loglevel error -t {maxDurationSeconds} -i \"{filePath}\" -vf \"fps={safeFps},scale=w=640:h=640:force_original_aspect_ratio=decrease\" -compression_level 1 \"{outputPattern}\"",
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                process.WaitForExit(45000);

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
