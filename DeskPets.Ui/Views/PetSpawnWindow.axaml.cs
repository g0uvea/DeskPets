using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

  private DispatcherTimer? _spinnerTimer;
  private double _spinnerAngle = 0;
  private double _aspectRatio = 1.0;
  private int _currentScale = 100;

  private void SetLoadingState(bool isLoading)
  {
    LoadingIndicator.IsVisible = isLoading;
    PetImage.Opacity = isLoading ? 0.6 : 1.0;

    if (isLoading)
    {
      LoadingText.Text = _language switch
      {
        "English" => "Loading...",
        "Español" => "Cargando...",
        _ => "Carregando..."
      };

      if (_spinnerTimer == null)
      {
        _spinnerTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Render, (s, e) =>
        {
          _spinnerAngle = (_spinnerAngle + 12) % 360;
          if (SpinnerArc.RenderTransform is RotateTransform rt)
          {
            rt.Angle = _spinnerAngle;
          }
        });
      }
      if (!_spinnerTimer.IsEnabled)
      {
        _spinnerTimer.Start();
      }
    }
    else
    {
      _spinnerTimer?.Stop();
    }
  }

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
    _currentScale = scale;

    // Detecta proporção (aspect ratio) da mídia para crop perfeito da janela
    if (pet.Thumbnail != null && pet.Thumbnail.PixelSize.Height > 0)
    {
      _aspectRatio = (double)pet.Thumbnail.PixelSize.Width / pet.Thumbnail.PixelSize.Height;
    }
    else if (pet.MediaHeight > 0 && pet.MediaWidth > 0)
    {
      _aspectRatio = (double)pet.MediaWidth / pet.MediaHeight;
    }
    else if (pet.CachedRawFrames.Count > 0 && pet.CachedRawFrames[0].Height > 0)
    {
      _aspectRatio = (double)pet.CachedRawFrames[0].Width / pet.CachedRawFrames[0].Height;
    }

    // Aplica o crop exato da janela mantendo a escala configurada ampla
    ApplyWindowSize(_currentScale);

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
  /// Aplica as dimensões da janela rigorosamente recortadas (crop) de acordo com a proporção do vídeo/imagem.
  /// A maior dimensão respeita o tamanho base e a escala, garantindo que o pet fique grande e nítido
  /// sem nenhuma área transparente ociosa nas laterais ou topo/fundo que bloqueie cliques no desktop.
  /// </summary>
  private void ApplyWindowSize(int scale)
  {
    int targetBound = Math.Max(64, (int)(BasePetSize * scale / 100.0));
    int finalW, finalH;

    if (_aspectRatio <= 0.001)
    {
      finalW = targetBound;
      finalH = targetBound;
    }
    else if (_aspectRatio < 1.0)
    {
      // Mídia Vertical (ex: Shorts 9:16)
      // A altura preenche o targetBound e a largura é ajustada na proporção exata
      finalH = targetBound;
      finalW = Math.Max(48, (int)Math.Round(targetBound * _aspectRatio));
    }
    else if (_aspectRatio > 1.0)
    {
      // Mídia Horizontal (ex: Widescreen 16:9)
      // A largura preenche o targetBound e a altura é ajustada na proporção exata
      finalW = targetBound;
      finalH = Math.Max(48, (int)Math.Round(targetBound / _aspectRatio));
    }
    else
    {
      // Mídia Quadrada (1:1)
      finalW = targetBound;
      finalH = targetBound;
    }

    Width = finalW;
    Height = finalH;
    MinWidth = finalW;
    MaxWidth = finalW;
    MinHeight = finalH;
    MaxHeight = finalH;
  }

  private void UpdateDimensionsFromBitmap(Bitmap? bitmap)
  {
    if (bitmap == null || bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0) return;

    double newAspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
    if (Math.Abs(newAspect - _aspectRatio) > 0.01)
    {
      _aspectRatio = newAspect;
      ApplyWindowSize(_currentScale);
    }
  }

  /// <summary>
  /// Atualiza em tempo real a escala do pet flutuante caso o usuário ajuste o slider nas opções.
  /// </summary>
  public void UpdateScale(int scale)
  {
    _currentScale = scale;
    ApplyWindowSize(_currentScale);
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

  private async void InitializePlayback(AssetItemViewModel pet)
  {
    bool hasActiveChromaKey = pet.ChromaKey != null && pet.ChromaKey.IsEnabled;

    // 1. Se NÃO tem chroma key ativado e o card da biblioteca já tem frames prontos, importa em 0ms!
    if (!hasActiveChromaKey && pet.HasPreviewFrames)
    {
      SetLoadingState(false);
      ImportFramesFromPet(pet);
      pet.PreviewFramesUpdated += OnPetFramesUpdated;
      return;
    }

    // Se ainda precisa carregar da mídia ou processar efeitos, ativa animação de carregamento
    SetLoadingState(true);

    try
    {
      // Se o pet ainda estiver carregando os quadros em background, aguarda finalizar
      if (!pet.HasPreviewFrames && !string.IsNullOrEmpty(pet.FilePath))
      {
        await pet.WaitForFramesLoadedAsync();
      }

      // Se não tem chroma key e agora tem frames:
      if (!hasActiveChromaKey && pet.HasPreviewFrames)
      {
        ImportFramesFromPet(pet);
        pet.PreviewFramesUpdated += OnPetFramesUpdated;
        return;
      }

      // Se tem chroma key OU se os frames precisam ser processados para este pet:
      if (pet.CachedRawFrames.Count > 0)
      {
        _rawFrames.Clear();
        _rawFrames.AddRange(pet.CachedRawFrames);
        await ApplyFramesToPlaybackAsync(_rawFrames, pet.ChromaKey);
        return;
      }

      // Caso contrário, carrega do arquivo
      if (!string.IsNullOrEmpty(pet.FilePath))
      {
        await LoadAnimationAsync(pet.FilePath, _fps);
      }
    }
    finally
    {
      SetLoadingState(false);
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

    if (_animationFrames.Count > 0)
    {
      UpdateDimensionsFromBitmap(_animationFrames[0].Bitmap);
    }

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
      var rawFrames = await MediaDecoder.ExtractFramesAsync(filePath, 60);
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
  /// Decodifica 100% dos quadros antes de iniciar para garantir reprodução suave do início ao fim sem travamentos.
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
      var dur = rf.Duration > TimeSpan.Zero ? rf.Duration : TimeSpan.FromMilliseconds(16);
      fullDuration += dur;
      fullRunningMs += (long)dur.TotalMilliseconds;
      fullCumulative.Add(fullRunningMs);
    }

    var rawSnapshot = rawFrames.ToArray();

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

    // Processa TODOS os quadros em paralelo multi-core (~300ms com Chroma Key para 700 frames)
    var processedFrames = await System.Threading.Tasks.Task.Run(() =>
    {
      var framesArray = new AnimationPlaybackFrame[rawSnapshot.Length];
      System.Threading.Tasks.Parallel.For(0, rawSnapshot.Length, i =>
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

        try
        {
          using var ms = new MemoryStream(data);
          var bmp = new Bitmap(ms);
          var dur = rawSnapshot[i].Duration > TimeSpan.Zero ? rawSnapshot[i].Duration : TimeSpan.FromMilliseconds(16);
          framesArray[i] = new AnimationPlaybackFrame(bmp, dur);
        }
        catch { }
      });

      var list = new List<AnimationPlaybackFrame>(rawSnapshot.Length);
      for (int i = 0; i < framesArray.Length; i++)
      {
        if (framesArray[i].Bitmap != null)
        {
          list.Add(framesArray[i]);
        }
      }
      return list;
    });

    if (processedFrames.Count == 0) return;

    await Dispatcher.UIThread.InvokeAsync(() =>
    {
      _animationFrames.Clear();
      _animationFrames.AddRange(processedFrames);

      if (processedFrames.Count > 0)
      {
        UpdateDimensionsFromBitmap(processedFrames[0].Bitmap);
      }

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
  }

  public async void ApplyCurrentChromaKey(ChromaKeyConfig? config)
  {
    if (_rawFrames.Count > 0)
    {
      SetLoadingState(true);
      try
      {
        await ApplyFramesToPlaybackAsync(_rawFrames, config);
      }
      finally
      {
        SetLoadingState(false);
      }
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
    _spinnerTimer?.Stop();
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
    _spinnerTimer?.Stop();

    _animationFrames.Clear();
    _cumulativeFrameEndMs.Clear();
    _rawFrames.Clear();
    PetImage.Source = null;
    base.OnClosed(e);
  }
}
