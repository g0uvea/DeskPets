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
using DeskPets.Ui.ViewModels;

namespace DeskPets.Ui.Views;

public partial class PetSpawnWindow : Window
{
    private static int _spawnOffsetCounter = 0;

    private DispatcherTimer? _animationTimer;
    private List<Bitmap> _frames = new();
    private int _currentFrameIndex = 0;
    private int _fps = 60;

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

    public PetSpawnWindow(AssetItemViewModel pet, int fps, int scale, bool isAlwaysOnTop, bool enableAnimations) : this()
    {
        Pet = pet;
        _fps = Math.Clamp(fps, 1, 60);
        IsAnimationsEnabled = enableAnimations;
        Topmost = isAlwaysOnTop;

        // Dimensões com trava de Min/Max para forçar modo flutuante em Tiling WMs no Linux (i3, sway, bspwm)
        int size = Math.Max(48, (int)(140.0 * scale / 100.0));
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

        // Carrega quadros animados se houver arquivo
        if (!string.IsNullOrEmpty(pet.FilePath))
        {
            _ = LoadAnimationAsync(pet.FilePath, _fps);
        }

        // Posiciona inteligentemente próximo à barra de tarefas / dock
        Opened += (s, e) => PositionNearTaskbar();
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
            _animationTimer?.Stop();
            if (_frames.Count > 0)
            {
                PetImage.Source = _frames[0];
            }
        }
        else
        {
            if (_frames.Count > 1 && (_animationTimer == null || !_animationTimer.IsEnabled))
            {
                StartAnimationTimer();
            }
        }
    }

    public void UpdateFps(int fps)
    {
        _fps = Math.Clamp(fps, 1, 60);
        if (_animationTimer != null)
        {
            _animationTimer.Interval = TimeSpan.FromSeconds(1.0 / _fps);
        }
    }

    private async System.Threading.Tasks.Task LoadAnimationAsync(string filePath, int fps)
    {
        try
        {
            var rawFrames = await MediaDecoder.ExtractFramesAsync(filePath, Math.Max(1, fps));
            if (rawFrames.Count > 1)
            {
                var bitmaps = new List<Bitmap>();
                foreach (var rf in rawFrames)
                {
                    using var ms = new MemoryStream(rf.Data);
                    bitmaps.Add(new Bitmap(ms));
                }

                _frames = bitmaps;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    FallbackIcon.IsVisible = false;
                    if (IsAnimationsEnabled)
                    {
                        StartAnimationTimer();
                    }
                    else if (_frames.Count > 0)
                    {
                        PetImage.Source = _frames[0];
                    }
                });
            }
            else if (rawFrames.Count == 1)
            {
                using var ms = new MemoryStream(rawFrames[0].Data);
                var singleBitmap = new Bitmap(ms);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    PetImage.Source = singleBitmap;
                    FallbackIcon.IsVisible = false;
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PetSpawnWindow] Erro ao carregar animação: {ex.Message}");
        }
    }

    private void StartAnimationTimer()
    {
        _animationTimer?.Stop();
        int safeFps = Math.Clamp(_fps, 1, 60);
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / safeFps)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_frames.Count == 0) return;

        _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Count;
        PetImage.Source = _frames[_currentFrameIndex];
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
        _animationTimer?.Stop();
        foreach (var bmp in _frames)
        {
            bmp.Dispose();
        }
        _frames.Clear();
        base.OnClosed(e);
    }
}
