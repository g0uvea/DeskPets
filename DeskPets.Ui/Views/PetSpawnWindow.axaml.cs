using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DeskPets.Core.Media;
using DeskPets.Core.Models;
using DeskPets.Effects;
using DeskPets.Ui.ViewModels;

namespace DeskPets.Ui.Views;

public partial class PetSpawnWindow : Window
{
  private const double BasePetSize = 600.0;
  private static int _spawnOffsetCounter = 0;

  private readonly struct AnimationPlaybackFrame
  {
    public Bitmap Bitmap { get; }
    public TimeSpan Duration { get; }

    public AnimationPlaybackFrame(Bitmap bitmap, TimeSpan duration)
    {
      Bitmap = bitmap;
      Duration = duration;
    }
  }

  private DispatcherTimer? _animationTimer;
  private readonly List<AnimationPlaybackFrame> _animationFrames = new();
  private readonly List<long> _cumulativeFrameEndMs = new();
  private readonly List<MediaFrame> _rawFrames = new();
  private TimeSpan _totalAnimationDuration = TimeSpan.Zero;
  private readonly System.Diagnostics.Stopwatch _playbackStopwatch = new();
  private int _currentFrameIndex = 0;
  private int _fps = 60;
  private bool _isUsingSharedBitmaps = false;

  // Ações de persistência e localização
  private readonly Action? _onSavePetsRequested;
  private readonly string _language = "Português";

  // Variáveis para drag & drop com fallback multiplataforma
  private PixelPoint _dragStartWindowPos;
  private Point _dragStartPointerPos;
  private bool _isManualDragging = false;

  public bool IsAnimationsEnabled { get; private set; } = true;
  public AssetItemViewModel? Pet { get; }

  public PetSpawnWindow()
  {
    InitializeComponent();
  }

  public PetSpawnWindow(
      AssetItemViewModel pet,
      int fps,
      int scale,
      bool isAlwaysOnTop,
      bool enableAnimations,
      Action? onSavePetsRequested = null,
      string language = "Português") : this()
  {
    Pet = pet;
    _fps = Math.Clamp(fps, 0, 60);
    IsAnimationsEnabled = enableAnimations;
    Topmost = isAlwaysOnTop;
    _onSavePetsRequested = onSavePetsRequested;
    _language = language;

    // Dimensões com trava de Min/Max para forçar modo flutuante em Tiling WMs no Linux (i3, sway, bspwm)
    int size = Math.Max(64, (int)(BasePetSize * scale / 100.0));
    Width = size;
    Height = size;
    MinWidth = size;
    MaxWidth = size;
    MinHeight = size;
    MaxHeight = size;

    // Exibe thumbnail ou ícone fallback
    if (pet.Thumbnail != null)
    {
      PetImage.Source = pet.Thumbnail;
      FallbackIcon.IsVisible = false;
    }
    else
    {
      FallbackIcon.IsVisible = true;
    }

    // Inicia reprodução instantânea (< 10ms) aproveitando quadros já decodificados da biblioteca
    InitializePlayback(pet);

    // Posiciona inteligentemente próximo à barra de tarefas / dock
    Opened += (s, e) => PositionNearTaskbar();
  }

  /// <summary>
  /// Atualiza em tempo real a escala do pet flutuante caso o usuário ajuste o slider nas opções.
  /// </summary>
  public void UpdateScale(int scale)
  {
    int size = Math.Max(64, (int)(BasePetSize * scale / 100.0));
    Width = size;
    Height = size;
    MinWidth = size;
    MaxWidth = size;
    MinHeight = size;
    MaxHeight = size;
  }

  /// <summary>
  /// Posicionamento inicial que respeita a área de trabalho útil (Windows Taskbar, macOS Dock, Linux GNOME/KDE Panels).
  /// </summary>
  private void PositionNearTaskbar()
  {
    try
    {
      var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
      if (screen != null)
      {
        var workArea = screen.WorkingArea;
        double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;

        int petW = (int)(Width * scale);
        int petH = (int)(Height * scale);

        int marginX = (int)(60 * scale);
        int marginY = (int)(25 * scale);

        int baseX = workArea.X + workArea.Width - petW - marginX;
        int baseY = workArea.Y + workArea.Height - petH - marginY;

        // Offset dinâmico para múltiplos pets não sobreporem 100%
        int step = (_spawnOffsetCounter++ % 6);
        int offsetX = step * (int)(45 * scale);
        int offsetY = (step % 3) * (int)(20 * scale);

        int targetX = Math.Max(workArea.X, baseX - offsetX);
        int targetY = Math.Max(workArea.Y, baseY - offsetY);

        Position = new PixelPoint(targetX, targetY);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"[PetSpawnWindow] Erro ao posicionar pet: {ex.Message}");
    }
  }

  public void SetAnimationsEnabled(bool enabled)
  {
    IsAnimationsEnabled = enabled;
    if (!enabled)
    {
      _playbackStopwatch.Stop();
      _animationTimer?.Stop();
      if (_animationFrames.Count > 0)
      {
        PetImage.Source = _animationFrames[0].Bitmap;
      }
    }
    else
    {
      if (_animationFrames.Count > 1 && _fps > 0)
      {
        StartAnimationTimer();
      }
    }
  }

  public void UpdateFps(int fps)
  {
    _fps = Math.Clamp(fps, 0, 60);
    if (_fps <= 0)
    {
      _playbackStopwatch.Stop();
      _animationTimer?.Stop();
    }
    else
    {
      if (IsAnimationsEnabled && _animationFrames.Count > 1)
      {
        StartAnimationTimer();
      }
    }
  }

  private void InitializePlayback(AssetItemViewModel pet)
  {
    bool hasActiveChromaKey = pet.ChromaKey != null && pet.ChromaKey.IsEnabled;

    // 1. Se NÃO tem chroma key ativado e o card da biblioteca já tem frames prontos, importa em 0ms!
    if (!hasActiveChromaKey && pet.HasPreviewFrames)
    {
      ImportFramesFromPet(pet);
      pet.PreviewFramesUpdated += OnPetFramesUpdated;
      return;
    }

    // 2. Se tem chroma key OU se os frames precisam ser processados para este pet:
    if (pet.CachedRawFrames.Count > 0)
    {
      _rawFrames.Clear();
      _rawFrames.AddRange(pet.CachedRawFrames);
      _ = ApplyFramesToPlaybackAsync(_rawFrames, pet.ChromaKey);
      return;
    }

    // 3. Caso contrário, carrega do arquivo
    if (!string.IsNullOrEmpty(pet.FilePath))
    {
      _ = LoadAnimationAsync(pet.FilePath, _fps);
    }
  }

  private void ImportFramesFromPet(AssetItemViewModel pet)
  {
    if (pet.PreviewFrames.Count == 0) return;

    _animationFrames.Clear();
    _cumulativeFrameEndMs.Clear();

    long prevMs = 0;
    for (int i = 0; i < pet.PreviewFrames.Count; i++)
    {
      long currentEnd = i < pet.CumulativePreviewEndMs.Count ? pet.CumulativePreviewEndMs[i] : (prevMs + 33);
      long durationMs = Math.Max(16, currentEnd - prevMs);
      prevMs = currentEnd;

      _animationFrames.Add(new AnimationPlaybackFrame(
          pet.PreviewFrames[i],
          TimeSpan.FromMilliseconds(durationMs)
      ));
      _cumulativeFrameEndMs.Add(currentEnd);
    }

    _totalAnimationDuration = TimeSpan.FromMilliseconds(pet.TotalPreviewDurationMs);
    _rawFrames.Clear();
    _rawFrames.AddRange(pet.CachedRawFrames);

    FallbackIcon.IsVisible = false;
    _isUsingSharedBitmaps = true;

    if (IsAnimationsEnabled && _fps > 0 && _animationFrames.Count > 1)
    {
      _playbackStopwatch.Restart();
      StartAnimationTimer();
    }
    else if (_animationFrames.Count > 0)
    {
      PetImage.Source = _animationFrames[0].Bitmap;
    }
  }

  private void OnPetFramesUpdated()
  {
    Dispatcher.UIThread.Post(() =>
    {
      if (Pet != null && Pet.HasPreviewFrames && _isUsingSharedBitmaps)
      {
        if (Pet.PreviewFrames.Count > _animationFrames.Count)
        {
          ImportFramesFromPet(Pet);
        }
      }
    });
  }

  private async System.Threading.Tasks.Task LoadAnimationAsync(string filePath, int fps)
  {
    try
    {
      // Amostragem robusta com base em pelo menos 30 FPS para manter suavidade se o FPS for aumentado
      var rawFrames = await MediaDecoder.ExtractFramesAsync(filePath, Math.Max(30, fps));
      _rawFrames.Clear();
      _rawFrames.AddRange(rawFrames);

      await ApplyFramesToPlaybackAsync(_rawFrames, Pet?.ChromaKey);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"[PetSpawnWindow] Erro ao carregar animação: {ex.Message}");
    }
  }

  /// <summary>
  /// Processa os quadros em memória com ou sem Chroma Key utilizando paralelismo multi-core ultra-rápido.
  /// Define a duração completa da animação desde o início para eliminar qualquer repetição no começo do vídeo.
  /// </summary>
  private async System.Threading.Tasks.Task ApplyFramesToPlaybackAsync(List<MediaFrame> rawFrames, ChromaKeyConfig? chromaKey)
  {
    if (rawFrames.Count == 0) return;

    bool applyChroma = chromaKey != null && chromaKey.IsEnabled;
    _isUsingSharedBitmaps = false;

    // 1. Calcula a duração total real e acumulada do vídeo completo desde o início!
    TimeSpan fullDuration = TimeSpan.Zero;
    long fullRunningMs = 0;
    var fullCumulative = new List<long>();
    foreach (var rf in rawFrames)
    {
      var dur = rf.Duration > TimeSpan.Zero ? rf.Duration : TimeSpan.FromMilliseconds(33);
      fullDuration += dur;
      fullRunningMs += (long)dur.TotalMilliseconds;
      fullCumulative.Add(fullRunningMs);
    }

    // Feedback visual imediato: atualiza o 1º frame no UI thread em 1ms!
    if (applyChroma && rawFrames.Count > 0)
    {
      try
      {
        int currentSafeIdx = Math.Clamp(_currentFrameIndex, 0, rawFrames.Count - 1);
        var immediateKeyed = ChromaKeyProcessor.ApplyChromaKeyToPngBytes(
            rawFrames[currentSafeIdx].Data,
            chromaKey!.KeyColorHex,
            chromaKey.Similarity,
            chromaKey.Smoothness);

        if (immediateKeyed != null)
        {
          using var ms = new MemoryStream(immediateKeyed);
          PetImage.Source = new Bitmap(ms);
        }
      }
      catch { }
    }

    var rawSnapshot = rawFrames.ToArray();
    int initialBatchCount = Math.Min(15, rawSnapshot.Length);
    var processedBytes = new byte[rawSnapshot.Length][];

    // FASE 1: Processa o lote inicial de 15 quadros em paralelo (~10ms) para início imediato
    var initialFrames = await System.Threading.Tasks.Task.Run(() =>
    {
      System.Threading.Tasks.Parallel.For(0, initialBatchCount, i =>
          {
            byte[] data = rawSnapshot[i].Data;
            if (applyChroma)
            {
              var keyed = ChromaKeyProcessor.ApplyChromaKeyToPngBytes(
                      data,
                      chromaKey!.KeyColorHex,
                      chromaKey.Similarity,
                      chromaKey.Smoothness);

              if (keyed != null) data = keyed;
            }
            processedBytes[i] = data;
          });

      var framesList = new List<AnimationPlaybackFrame>();
      for (int i = 0; i < initialBatchCount; i++)
      {
        try
        {
          using var ms = new MemoryStream(processedBytes[i]);
          var bmp = new Bitmap(ms);
          var dur = rawSnapshot[i].Duration > TimeSpan.Zero ? rawSnapshot[i].Duration : TimeSpan.FromMilliseconds(33);
          framesList.Add(new AnimationPlaybackFrame(bmp, dur));
        }
        catch { }
      }

      return framesList;
    });

    if (initialFrames.Count == 0) return;

    await Dispatcher.UIThread.InvokeAsync(() =>
    {
      _animationFrames.Clear();
      _animationFrames.AddRange(initialFrames);

      // Importante: define a lista acumulada e a duração COMPLETA do vídeo desde já!
      _cumulativeFrameEndMs.Clear();
      _cumulativeFrameEndMs.AddRange(fullCumulative);
      _totalAnimationDuration = fullDuration;

      _currentFrameIndex = 0;
      _playbackStopwatch.Restart();

      FallbackIcon.IsVisible = false;
      if (IsAnimationsEnabled && _fps > 0 && _animationFrames.Count > 1)
      {
        StartAnimationTimer();
      }
      else if (_animationFrames.Count > 0)
      {
        PetImage.Source = _animationFrames[0].Bitmap;
      }
    });

    // FASE 2: Processa todos os quadros restantes em paralelo multi-core (~300ms) e anexa sem reiniciar
    if (rawSnapshot.Length > initialBatchCount)
    {
      _ = System.Threading.Tasks.Task.Run(async () =>
      {
        System.Threading.Tasks.Parallel.For(initialBatchCount, rawSnapshot.Length, i =>
              {
                byte[] data = rawSnapshot[i].Data;
                if (applyChroma)
                {
                  var keyed = ChromaKeyProcessor.ApplyChromaKeyToPngBytes(
                            data,
                            chromaKey!.KeyColorHex,
                            chromaKey.Similarity,
                            chromaKey.Smoothness);

                  if (keyed != null) data = keyed;
                }
                processedBytes[i] = data;
              });

        var remainingFrames = new List<AnimationPlaybackFrame>();
        for (int i = initialBatchCount; i < rawSnapshot.Length; i++)
        {
          try
          {
            using var ms = new MemoryStream(processedBytes[i]);
            var bmp = new Bitmap(ms);
            var dur = rawSnapshot[i].Duration > TimeSpan.Zero ? rawSnapshot[i].Duration : TimeSpan.FromMilliseconds(33);
            remainingFrames.Add(new AnimationPlaybackFrame(bmp, dur));
          }
          catch { }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
              {
                _animationFrames.AddRange(remainingFrames);
              });
      });
    }
  }

  public void ApplyCurrentChromaKey(ChromaKeyConfig? config)
  {
    if (_rawFrames.Count > 0)
    {
      _ = ApplyFramesToPlaybackAsync(_rawFrames, config);
    }
  }

  private void StartAnimationTimer()
  {
    _animationTimer?.Stop();
    if (_fps <= 0 || _animationFrames.Count <= 1 || !IsAnimationsEnabled)
    {
      _playbackStopwatch.Stop();
      return;
    }

    int safeFps = Math.Clamp(_fps, 1, 60);
    if (!_playbackStopwatch.IsRunning)
    {
      _playbackStopwatch.Start();
    }

    _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
    {
      Interval = TimeSpan.FromSeconds(1.0 / safeFps)
    };
    _animationTimer.Tick += OnAnimationTick;
    _animationTimer.Start();
  }

  private void OnAnimationTick(object? sender, EventArgs e)
  {
    if (_animationFrames.Count == 0 || _totalAnimationDuration <= TimeSpan.Zero) return;

    long totalMs = (long)_totalAnimationDuration.TotalMilliseconds;
    if (totalMs <= 0) return;

    // Baseado em tempo real: a animação avança na velocidade 1.0x padrão do vídeo completo
    long elapsedMs = _playbackStopwatch.ElapsedMilliseconds % totalMs;

    int targetIndex = 0;
    while (targetIndex < _cumulativeFrameEndMs.Count - 1 && elapsedMs >= _cumulativeFrameEndMs[targetIndex])
    {
      targetIndex++;
    }

    // Clampa com segurança para não estourar caso a Fase 2 ainda esteja anexando os quadros
    int safeIndex = Math.Clamp(targetIndex, 0, _animationFrames.Count - 1);

    if (safeIndex != _currentFrameIndex)
    {
      _currentFrameIndex = safeIndex;
      PetImage.Source = _animationFrames[_currentFrameIndex].Bitmap;
    }
  }

  private async void OnOpenChromaKeyClick(object? sender, RoutedEventArgs e)
  {
    byte[]? sampleFrameBytes = null;
    if (_rawFrames.Count > 0)
    {
      sampleFrameBytes = _rawFrames[0].Data;
    }

    var dialog = new ChromaKeyEditorDialog(
        sampleFrameBytes,
        Pet?.FilePath,
        Pet?.ChromaKey,
        _language);

    var result = await dialog.ShowDialog<ChromaKeyConfig?>(this);
    if (result != null && Pet != null)
    {
      Pet.ChromaKey = result;

      if (_rawFrames.Count > 0)
      {
        _ = ApplyFramesToPlaybackAsync(_rawFrames, result);
      }

      // Notifica persistência em pets.json para salvar configuração do pet
      _onSavePetsRequested?.Invoke();
    }
  }

  // Drag & Drop com suporte nativo e fallback para compositores e WMs customizados do Linux
  private void OnPetPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
    {
      try
      {
        // Mecanismo nativo do sistema operacional (Windows DWM, macOS Cocoa e Linux X11/Wayland)
        BeginMoveDrag(e);
      }
      catch
      {
        // Fallback manual para WMs antigos ou ambientes que rejeitem BeginMoveDrag
        _isManualDragging = true;
        _dragStartWindowPos = Position;
        _dragStartPointerPos = e.GetPosition(this);
      }
    }
  }

  private void OnPetPointerMoved(object? sender, PointerEventArgs e)
  {
    if (_isManualDragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
    {
      var currentPos = e.GetPosition(this);
      var deltaX = (int)(currentPos.X - _dragStartPointerPos.X);
      var deltaY = (int)(currentPos.Y - _dragStartPointerPos.Y);
      Position = new PixelPoint(_dragStartWindowPos.X + deltaX, _dragStartWindowPos.Y + deltaY);
    }
  }

  private void OnPetPointerReleased(object? sender, PointerReleasedEventArgs e)
  {
    _isManualDragging = false;
  }

  private void OnToggleTopmostClick(object? sender, RoutedEventArgs e)
  {
    Topmost = !Topmost;
  }

  private void OnClosePetClick(object? sender, RoutedEventArgs e)
  {
    _animationTimer?.Stop();
    Close();
  }

  protected override void OnClosed(EventArgs e)
  {
    if (Pet != null)
    {
      Pet.PreviewFramesUpdated -= OnPetFramesUpdated;
    }

    _playbackStopwatch.Stop();
    _animationTimer?.Stop();

    _animationFrames.Clear();
    _cumulativeFrameEndMs.Clear();
    _rawFrames.Clear();
    PetImage.Source = null;
    base.OnClosed(e);
  }
}
