using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
  private double _basePetWidth = 600.0;
  private double _basePetHeight = 600.0;
  private bool _flipH = false;
  private bool _flipV = false;
  private bool _desktopInteractionEnabled = false;
  private string _desktopInteractionType = "DVD";
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
        "日本語" => "読み込み中...",
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
      int baseWidth,
      int baseHeight,
      bool isAlwaysOnTop,
      Action? onSavePetsRequested = null,
      string language = "Português") : this()
  {
    Pet = pet;
    _fps = Math.Clamp(fps, 0, 60);
    Topmost = isAlwaysOnTop;
    _onSavePetsRequested = onSavePetsRequested;
    _language = language;
    _currentScale = scale;
    _basePetWidth = Math.Clamp(baseWidth, 200, 1000);
    _basePetHeight = Math.Clamp(baseHeight, 200, 1000);

    _flipH = pet.FlipHorizontal;
    _flipV = pet.FlipVertical;
    _desktopInteractionEnabled = pet.DesktopInteractionEnabled;
    _desktopInteractionType = !string.IsNullOrEmpty(pet.DesktopInteractionType) ? pet.DesktopInteractionType : "DVD";

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

    // Aplica flips e menu
    UpdateFlipTransforms();
    UpdateContextMenuUi();
    SetupDesktopInteraction();

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
  /// Respeita o tamanho base configurado e a escala, garantindo que o pet fique grande e nítido
  /// sem nenhuma área transparente ociosa nas laterais ou topo/fundo que bloqueie cliques no desktop.
  /// </summary>
  private void ApplyWindowSize(int scale)
  {
    double targetW = Math.Max(64, _basePetWidth * scale / 100.0);
    double targetH = Math.Max(64, _basePetHeight * scale / 100.0);
    int finalW, finalH;

    if (_aspectRatio <= 0.001)
    {
      finalW = (int)targetW;
      finalH = (int)targetH;
    }
    else if (_aspectRatio < 1.0)
    {
      // Mídia Vertical (ex: Shorts 9:16)
      // A altura preenche targetH e a largura é ajustada na proporção exata
      finalH = (int)targetH;
      finalW = Math.Max(48, (int)Math.Round(targetH * _aspectRatio));
    }
    else if (_aspectRatio > 1.0)
    {
      // Mídia Horizontal (ex: Widescreen 16:9)
      // A largura preenche targetW e a altura é ajustada na proporção exata
      finalW = (int)targetW;
      finalH = Math.Max(48, (int)Math.Round(targetW / _aspectRatio));
    }
    else
    {
      // Mídia Quadrada (1:1)
      finalW = (int)targetW;
      finalH = (int)targetH;
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
    UpdateContextMenuUi();
  }

  private void OnSetScaleClick(object? sender, RoutedEventArgs e)
  {
    if (sender is MenuItem item && int.TryParse(item.CommandParameter?.ToString(), out int scale))
    {
      _currentScale = scale;
      ApplyWindowSize(_currentScale);
      if (Pet != null)
      {
        Pet.Scale = scale;
        _onSavePetsRequested?.Invoke();
      }
      UpdateContextMenuUi();
    }
  }

  private void OnSetFpsClick(object? sender, RoutedEventArgs e)
  {
    if (sender is MenuItem item && int.TryParse(item.CommandParameter?.ToString(), out int fps))
    {
      UpdateFps(fps);
      if (Pet != null)
      {
        Pet.Fps = fps;
        _onSavePetsRequested?.Invoke();
      }
      UpdateContextMenuUi();
    }
  }

  private void OnToggleFlipHClick(object? sender, RoutedEventArgs e)
  {
    _flipH = !_flipH;
    UpdateFlipTransforms();
    if (Pet != null)
    {
      Pet.FlipHorizontal = _flipH;
      _onSavePetsRequested?.Invoke();
    }
    UpdateContextMenuUi();
  }

  private void OnToggleFlipVClick(object? sender, RoutedEventArgs e)
  {
    _flipV = !_flipV;
    UpdateFlipTransforms();
    if (Pet != null)
    {
      Pet.FlipVertical = _flipV;
      _onSavePetsRequested?.Invoke();
    }
    UpdateContextMenuUi();
  }

  private void OnToggleInteractionClick(object? sender, RoutedEventArgs e)
  {
    _desktopInteractionEnabled = !_desktopInteractionEnabled;
    if (Pet != null)
    {
      Pet.DesktopInteractionEnabled = _desktopInteractionEnabled;
      _onSavePetsRequested?.Invoke();
    }
    SetupDesktopInteraction();
    UpdateContextMenuUi();
  }

  private void OnSelectInteractionModeClick(object? sender, RoutedEventArgs e)
  {
    if (sender is MenuItem item && item.CommandParameter is string mode)
    {
      _desktopInteractionType = mode;
      _desktopInteractionEnabled = true;
      if (Pet != null)
      {
        Pet.DesktopInteractionType = mode;
        Pet.DesktopInteractionEnabled = true;
        _onSavePetsRequested?.Invoke();
      }
      SetupDesktopInteraction();
      UpdateContextMenuUi();
    }
  }

  private void UpdateFlipTransforms()
  {
    if (PetImage?.RenderTransform is ScaleTransform st)
    {
      st.ScaleX = _flipH ? -1.0 : 1.0;
      st.ScaleY = _flipV ? -1.0 : 1.0;
    }
    else if (PetImage != null)
    {
      PetImage.RenderTransform = new ScaleTransform(_flipH ? -1.0 : 1.0, _flipV ? -1.0 : 1.0);
    }
    if (CheckFlipH != null) CheckFlipH.IsChecked = _flipH;
    if (CheckFlipV != null) CheckFlipV.IsChecked = _flipV;
  }

  private void UpdateContextMenuUi()
  {
    if (MenuAlwaysOnTop == null) return;

    MenuAlwaysOnTop.Header = _language switch
    {
      "English" => "Always on Top",
      "日本語" => "常に最前面に表示",
      _ => "Sempre no Topo"
    };

    MenuScale.Header = _language switch
    {
      "English" => "Scale",
      "日本語" => "拡大率",
      _ => "Escala"
    };

    MenuFps.Header = _language switch
    {
      "English" => "Frame Rate (FPS)",
      "日本語" => "フレームレート (FPS)",
      _ => "Taxa de Quadros (FPS)"
    };

    MenuFps0.Header = _language switch
    {
      "English" => "0 FPS (Pause)",
      "日本語" => "0 FPS (一時停止)",
      _ => "0 FPS (Pausar)"
    };

    MenuFlipH.Header = _language switch
    {
      "English" => "Flip Horizontal",
      "日本語" => "左右反転",
      _ => "Inverter Horizontalmente"
    };

    MenuFlipV.Header = _language switch
    {
      "English" => "Flip Vertical",
      "日本語" => "上下反転",
      _ => "Inverter Verticalmente"
    };

    MenuChromaKey.Header = _language switch
    {
      "English" => "Chroma Key (Transparent Background)",
      "日本語" => "クロマキー (背景透過)",
      _ => "Chroma Key (Fundo Transparente)"
    };

    MenuDesktopInteraction.Header = _language switch
    {
      "English" => "Desktop Interaction",
      "日本語" => "デスクトップ操作",
      _ => "Interação com o Desktop"
    };

    MenuInteractionToggle.Header = _language switch
    {
      "English" => "Enable Interaction",
      "日本語" => "操作を有効化",
      _ => "Ativar Interação"
    };

    MenuModeDvd.Header = _language switch
    {
      "English" => "Bounce Screen (DVD Style)",
      "日本語" => "画面バウンド (DVD風)",
      _ => "Quicar na Tela (Sinal de DVD)"
    };

    MenuModeWander.Header = _language switch
    {
      "English" => "Wander Screen (Random)",
      "日本語" => "画面を散歩 (ランダム)",
      _ => "Passear pela Tela (Aleatório)"
    };

    MenuModeMischief.Header = _language switch
    {
      "English" => "Mischief Mode (Move Mouse)",
      "日本語" => "いたずらモード (マウス移動)",
      _ => "Modo Travessura (Mover Mouse)"
    };

    MenuClosePet.Header = _language switch
    {
      "English" => "Close this Pet",
      "日本語" => "このペットを閉じる",
      _ => "Fechar este Pet"
    };

    if (CheckTopmost != null) CheckTopmost.IsChecked = Topmost;

    if (CheckScale50 != null) CheckScale50.IsChecked = _currentScale == 50;
    if (CheckScale75 != null) CheckScale75.IsChecked = _currentScale == 75;
    if (CheckScale100 != null) CheckScale100.IsChecked = _currentScale == 100;
    if (CheckScale125 != null) CheckScale125.IsChecked = _currentScale == 125;
    if (CheckScale150 != null) CheckScale150.IsChecked = _currentScale == 150;
    if (CheckScale175 != null) CheckScale175.IsChecked = _currentScale == 175;
    if (CheckScale200 != null) CheckScale200.IsChecked = _currentScale == 200;

    if (CheckFps0 != null) CheckFps0.IsChecked = _fps == 0;
    if (CheckFps15 != null) CheckFps15.IsChecked = _fps == 15;
    if (CheckFps24 != null) CheckFps24.IsChecked = _fps == 24;
    if (CheckFps30 != null) CheckFps30.IsChecked = _fps == 30;
    if (CheckFps60 != null) CheckFps60.IsChecked = _fps == 60;

    if (CheckFlipH != null) CheckFlipH.IsChecked = _flipH;
    if (CheckFlipV != null) CheckFlipV.IsChecked = _flipV;

    if (CheckInteractionEnabled != null) CheckInteractionEnabled.IsChecked = _desktopInteractionEnabled;
    if (CheckModeDvd != null) CheckModeDvd.IsChecked = _desktopInteractionType == "DVD";
    if (CheckModeWander != null) CheckModeWander.IsChecked = _desktopInteractionType == "WANDER";
    if (CheckModeMischief != null) CheckModeMischief.IsChecked = _desktopInteractionType == "MISCHIEF";

    MenuModeDvd.IsEnabled = _desktopInteractionEnabled;
    MenuModeWander.IsEnabled = _desktopInteractionEnabled;
    MenuModeMischief.IsEnabled = _desktopInteractionEnabled;
  }

  // === Desktop Interaction Implementation ===
  private DispatcherTimer? _interactionTimer;
  private double _interactionVx = 3.0;
  private double _interactionVy = 2.5;
  private double _interactionPosX;
  private double _interactionPosY;
  private PixelPoint? _wanderTarget;
  private DateTime _wanderIdleUntil = DateTime.MinValue;
  private DateTime _lastMischiefAction = DateTime.UtcNow;
  private readonly Random _random = new();

  private void SetupDesktopInteraction()
  {
    _interactionTimer?.Stop();
    if (!_desktopInteractionEnabled) return;

    _interactionPosX = Position.X;
    _interactionPosY = Position.Y;

    _interactionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Render, OnInteractionTick);
    _interactionTimer.Start();
  }

  private void OnInteractionTick(object? sender, EventArgs e)
  {
    if (!_desktopInteractionEnabled || _isManualDragging)
    {
      _interactionPosX = Position.X;
      _interactionPosY = Position.Y;
      return;
    }

    var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
    if (screen == null) return;
    var wa = screen.WorkingArea;
    double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
    int petW = (int)(Width * scale);
    int petH = (int)(Height * scale);

    switch (_desktopInteractionType)
    {
      case "DVD":
        TickDvdBounce(wa, petW, petH);
        break;
      case "WANDER":
        TickWander(wa, petW, petH);
        break;
      case "MISCHIEF":
        TickMischief(wa, petW, petH);
        break;
    }
  }

  private void TickDvdBounce(PixelRect wa, int petW, int petH)
  {
    _interactionPosX += _interactionVx;
    _interactionPosY += _interactionVy;

    if (_interactionPosX <= wa.X)
    {
      _interactionPosX = wa.X;
      _interactionVx = Math.Abs(_interactionVx);
    }
    else if (_interactionPosX + petW >= wa.X + wa.Width)
    {
      _interactionPosX = wa.X + wa.Width - petW;
      _interactionVx = -Math.Abs(_interactionVx);
    }

    if (_interactionPosY <= wa.Y)
    {
      _interactionPosY = wa.Y;
      _interactionVy = Math.Abs(_interactionVy);
    }
    else if (_interactionPosY + petH >= wa.Y + wa.Height)
    {
      _interactionPosY = wa.Y + wa.Height - petH;
      _interactionVy = -Math.Abs(_interactionVy);
    }

    Position = new PixelPoint((int)_interactionPosX, (int)_interactionPosY);
  }

  private void TickWander(PixelRect wa, int petW, int petH)
  {
    if (DateTime.UtcNow < _wanderIdleUntil)
    {
      return;
    }

    if (_wanderTarget == null)
    {
      int minX = wa.X + 20;
      int maxX = Math.Max(minX + 1, wa.X + wa.Width - petW - 20);
      int minY = wa.Y + 20;
      int maxY = Math.Max(minY + 1, wa.Y + wa.Height - petH - 20);

      _wanderTarget = new PixelPoint(_random.Next(minX, maxX), _random.Next(minY, maxY));
    }

    double dx = _wanderTarget.Value.X - _interactionPosX;
    double dy = _wanderTarget.Value.Y - _interactionPosY;
    double dist = Math.Sqrt(dx * dx + dy * dy);

    if (dist < 6.0)
    {
      _interactionPosX = _wanderTarget.Value.X;
      _interactionPosY = _wanderTarget.Value.Y;
      Position = new PixelPoint((int)_interactionPosX, (int)_interactionPosY);
      _wanderTarget = null;
      _wanderIdleUntil = DateTime.UtcNow.AddSeconds(_random.Next(2, 6));
      return;
    }

    double speed = 2.5;
    double stepX = (dx / dist) * speed;
    double stepY = (dy / dist) * speed;

    _interactionPosX += stepX;
    _interactionPosY += stepY;
    Position = new PixelPoint((int)_interactionPosX, (int)_interactionPosY);
  }

  private void TickMischief(PixelRect wa, int petW, int petH)
  {
    if ((DateTime.UtcNow - _lastMischiefAction).TotalSeconds > 5.0)
    {
      _lastMischiefAction = DateTime.UtcNow;

      int minX = wa.X + 30;
      int maxX = Math.Max(minX + 1, wa.X + wa.Width - petW - 30);
      int minY = wa.Y + 30;
      int maxY = Math.Max(minY + 1, wa.Y + wa.Height - petH - 30);

      _interactionPosX = _random.Next(minX, maxX);
      _interactionPosY = _random.Next(minY, maxY);
      Position = new PixelPoint((int)_interactionPosX, (int)_interactionPosY);

      TryWarpMouse((int)_interactionPosX + petW / 2, (int)_interactionPosY + petH / 2);
    }
  }

  protected override void OnPointerEntered(PointerEventArgs e)
  {
    base.OnPointerEntered(e);
    if (_desktopInteractionEnabled && _desktopInteractionType == "MISCHIEF")
    {
      var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
      if (screen != null)
      {
        var wa = screen.WorkingArea;
        double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        int petW = (int)(Width * scale);
        int petH = (int)(Height * scale);
        _interactionPosX = Math.Clamp(_interactionPosX + _random.Next(-200, 200), wa.X, wa.X + wa.Width - petW);
        _interactionPosY = Math.Clamp(_interactionPosY + _random.Next(-150, 150), wa.Y, wa.Y + wa.Height - petH);
        Position = new PixelPoint((int)_interactionPosX, (int)_interactionPosY);
      }
    }
  }

  private void TryWarpMouse(int targetX, int targetY)
  {
    try
    {
      if (OperatingSystem.IsWindows())
      {
        SetCursorPos(targetX, targetY);
      }
      else if (OperatingSystem.IsLinux())
      {
        IntPtr display = XOpenDisplay(null);
        if (display != IntPtr.Zero)
        {
          try
          {
            XWarpPointer(display, IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, targetX, targetY);
            XFlush(display);
          }
          finally
          {
            XCloseDisplay(display);
          }
        }
      }
    }
    catch
    {
      // Fallback seguro se não suportado pelo ambiente/Wayland
    }
  }

  [DllImport("user32.dll")]
  private static extern bool SetCursorPos(int X, int Y);

  [DllImport("libX11.so.6")]
  private static extern IntPtr XOpenDisplay(string? display_name);

  [DllImport("libX11.so.6")]
  private static extern int XWarpPointer(IntPtr display, IntPtr src_w, IntPtr dest_w, int src_x, int src_y, uint src_width, uint src_height, int dest_x, int dest_y);

  [DllImport("libX11.so.6")]
  private static extern int XFlush(IntPtr display);

  [DllImport("libX11.so.6")]
  private static extern int XCloseDisplay(IntPtr display);

  private void OnClosePetClick(object? sender, RoutedEventArgs e)
  {
    _animationTimer?.Stop();
    _spinnerTimer?.Stop();
    _interactionTimer?.Stop();
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
    _interactionTimer?.Stop();

    _animationFrames.Clear();
    _cumulativeFrameEndMs.Clear();
    _rawFrames.Clear();
    PetImage.Source = null;
    base.OnClosed(e);
  }
}
