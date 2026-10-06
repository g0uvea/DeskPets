using System;
using System.Collections.Generic;
using System.IO;
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
    private DispatcherTimer? _animationTimer;
    private List<Bitmap> _frames = new();
    private int _currentFrameIndex = 0;

    public PetSpawnWindow()
    {
        InitializeComponent();
    }

    public PetSpawnWindow(AssetItemViewModel pet, int fps, int scale) : this()
    {
        int size = Math.Max(64, (int)(140.0 * scale / 100.0));
        Width = size;
        Height = size;

        // Se o pet já tiver thumbnail, exibe imediatamente
        if (pet.Thumbnail != null)
        {
            PetImage.Source = pet.Thumbnail;
        }

        // Carrega quadros animados se houver arquivo
        if (!string.IsNullOrEmpty(pet.FilePath))
        {
            _ = LoadAnimationAsync(pet.FilePath, fps);
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
                int safeFps = Math.Clamp(fps, 1, 60);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _animationTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.0 / safeFps)
                    };
                    _animationTimer.Tick += OnAnimationTick;
                    _animationTimer.Start();
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PetSpawnWindow] Erro ao carregar animação: {ex.Message}");
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_frames.Count == 0) return;

        _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Count;
        PetImage.Source = _frames[_currentFrameIndex];
    }

    private void OnPetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
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
