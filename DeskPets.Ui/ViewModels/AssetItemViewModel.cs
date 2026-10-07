using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskPets.Core.Media;
using DeskPets.Core.Models;
using DeskPets.Effects;

namespace DeskPets.Ui.ViewModels;

public partial class AssetItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _name = "Asset";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string _mediaType = "Image";

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private ChromaKeyConfig? _chromaKey;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpawned))]
    private int _activeInstanceCount = 0;

    public bool IsSpawned => ActiveInstanceCount > 0;

    [ObservableProperty]
    private bool _isCardHighlighted = false;

    private readonly List<Bitmap> _previewFrames = new();
    private readonly List<long> _cumulativePreviewEndMs = new();
    private long _totalPreviewDurationMs = 0;
    private int _previewFrameIndex = 0;
    private readonly List<MediaFrame> _cachedRawFrames = new();

    public IReadOnlyList<MediaFrame> CachedRawFrames => _cachedRawFrames;
    public IReadOnlyList<Bitmap> PreviewFrames => _previewFrames;
    public IReadOnlyList<long> CumulativePreviewEndMs => _cumulativePreviewEndMs;
    public long TotalPreviewDurationMs => _totalPreviewDurationMs;
    public bool HasPreviewFrames => _previewFrames.Count > 1;
    public event Action? PreviewFramesUpdated;

    public AssetItemViewModel(int id, string name = "Asset", string? filePath = null, ChromaKeyConfig? chromaKey = null)
    {
        _id = id;
        _name = name;
        _filePath = filePath;
        _chromaKey = chromaKey;

        if (!string.IsNullOrEmpty(filePath))
        {
            _mediaType = MediaDecoder.DetectMediaType(filePath);
            _ = LoadMediaAsync();
        }
    }

    public async Task LoadMediaAsync()
    {
        await LoadThumbnailAsync();
        if (MediaType != "Image")
        {
            await LoadPreviewFramesAsync();
        }
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            return;

        try
        {
            byte[]? thumbBytes = await MediaDecoder.ExtractThumbnailBytesAsync(FilePath, 140, 140);
            if (thumbBytes != null && thumbBytes.Length > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(thumbBytes);
                        Thumbnail = new Bitmap(ms);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AssetItemViewModel] Erro ao instanciar Bitmap: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetItemViewModel] Erro ao carregar thumbnail para {FilePath}: {ex.Message}");
        }
    }

    public async Task LoadPreviewFramesAsync()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            return;

        try
        {
            // Extrai quadros da mídia completa (até 30s)
            if (_cachedRawFrames.Count == 0)
            {
                var raw = await MediaDecoder.ExtractFramesAsync(FilePath, 60, 30);
                if (raw.Count > 0)
                {
                    _cachedRawFrames.Clear();
                    _cachedRawFrames.AddRange(raw);
                }
            }

            if (_cachedRawFrames.Count > 1)
            {
                var rawSnapshot = _cachedRawFrames.ToArray();

                // FASE 1: Decodifica imediatamente os primeiros quadros (ex: 12 frames ~= 400ms de vídeo)
                // Inicia a animação no card da biblioteca em menos de 50ms (sem filtros, mídia original limpa)
                int initialBatchCount = Math.Min(12, rawSnapshot.Length);
                var (initialBitmaps, initialCum, initialTotalMs) = await Task.Run(() =>
                {
                    var bmps = new List<Bitmap>();
                    var cum = new List<long>();
                    long running = 0;

                    for (int i = 0; i < initialBatchCount; i++)
                    {
                        var rf = rawSnapshot[i];
                        using var ms = new MemoryStream(rf.Data);
                        bmps.Add(new Bitmap(ms));

                        long frameMs = (long)(rf.Duration > TimeSpan.Zero ? rf.Duration.TotalMilliseconds : 33);
                        running += frameMs;
                        cum.Add(running);
                    }
                    return (bmps, cum, running);
                });

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ClearPreviewFrames();
                    _previewFrames.AddRange(initialBitmaps);
                    _cumulativePreviewEndMs.AddRange(initialCum);
                    _totalPreviewDurationMs = initialTotalMs;
                    _previewFrameIndex = 0;
                    if (_previewFrames.Count > 0)
                    {
                        Thumbnail = _previewFrames[0];
                    }
                    PreviewFramesUpdated?.Invoke();
                });

                // FASE 2: Processa o restante dos quadros em background e anexa sem congelar a reprodução
                if (rawSnapshot.Length > initialBatchCount)
                {
                    _ = Task.Run(async () =>
                    {
                        var remainingBitmaps = new List<Bitmap>();
                        var remainingCum = new List<long>();
                        long running = initialTotalMs;

                        for (int i = initialBatchCount; i < rawSnapshot.Length; i++)
                        {
                            var rf = rawSnapshot[i];
                            using var ms = new MemoryStream(rf.Data);
                            remainingBitmaps.Add(new Bitmap(ms));

                            long frameMs = (long)(rf.Duration > TimeSpan.Zero ? rf.Duration.TotalMilliseconds : 33);
                            running += frameMs;
                            remainingCum.Add(running);
                        }

                        long finalTotalMs = running;

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            _previewFrames.AddRange(remainingBitmaps);
                            _cumulativePreviewEndMs.AddRange(remainingCum);
                            _totalPreviewDurationMs = finalTotalMs;
                            PreviewFramesUpdated?.Invoke();
                        });
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetItemViewModel] Erro ao carregar preview frames para {FilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Avança a pré-visualização sincronizada com o tempo real decorrido, garantindo 1.0x de velocidade e respeitando qualquer FPS (15..60).
    /// </summary>
    public void AdvancePreviewFrame(long elapsedMs)
    {
        if (_previewFrames.Count <= 1 || _totalPreviewDurationMs <= 0) return;

        long loopMs = elapsedMs % _totalPreviewDurationMs;
        int targetIndex = 0;
        int maxIndex = Math.Min(_previewFrames.Count - 1, _cumulativePreviewEndMs.Count - 1);
        while (targetIndex < maxIndex && loopMs >= _cumulativePreviewEndMs[targetIndex])
        {
            targetIndex++;
        }

        if (targetIndex >= 0 && targetIndex < _previewFrames.Count && targetIndex != _previewFrameIndex)
        {
            _previewFrameIndex = targetIndex;
            Thumbnail = _previewFrames[_previewFrameIndex];
        }
    }

    public void AdvancePreviewFrame()
    {
        if (_previewFrames.Count > 1)
        {
            _previewFrameIndex = (_previewFrameIndex + 1) % _previewFrames.Count;
            Thumbnail = _previewFrames[_previewFrameIndex];
        }
    }

    /// <summary>
    /// Re-processa thumbnail e preview frames em background utilizando os quadros em cache na memória (zero chamadas extras ao FFmpeg).
    /// </summary>
    public async Task ReloadMediaWithChromaKeyAsync()
    {
        await LoadThumbnailAsync();
        if (MediaType != "Image")
        {
            await LoadPreviewFramesAsync();
        }
    }

    private void ClearPreviewFrames()
    {
        _previewFrames.Clear();
        _cumulativePreviewEndMs.Clear();
        _totalPreviewDurationMs = 0;
        _previewFrameIndex = 0;
    }
}
