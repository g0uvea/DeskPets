using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeskPets.Core.Media;
using DeskPets.Ui.ViewModels;

namespace DeskPets.Ui.Views;

public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
    }

    private Window? GetParentWindow() => TopLevel.GetTopLevel(this) as Window;

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var window = GetParentWindow();
        if (window == null) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize(window);
            }
            else
            {
                window.BeginMoveDrag(e);
            }
        }
    }

    // Azul: Abrir FilePicker para Adicionar Pets Locais
    private async void OnAddPetClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Selecionar Pets / Assets",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Todos os Formatos Suportados")
                    {
                        Patterns = new[] { "*.png", "*.gif", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.mp4", "*.webm", "*.mov", "*.av1", "*.mkv", "*.avi" }
                    },
                    new FilePickerFileType("Imagens e Animações (*.png, *.gif, *.webp, *.jpg)")
                    {
                        Patterns = new[] { "*.png", "*.gif", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                    },
                    new FilePickerFileType("Vídeos (*.mp4, *.webm, *.mov, *.av1)")
                    {
                        Patterns = new[] { "*.mp4", "*.webm", "*.mov", "*.av1", "*.mkv", "*.avi" }
                    },
                    FilePickerFileTypes.All
                }
            });

            if (files != null && files.Count > 0 && DataContext is MainViewModel vm)
            {
                var parentWindow = GetParentWindow();
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file.Name);
                    string? localPath = file.TryGetLocalPath();

                    // Se for vídeo, verifica a duração e avisa caso exceda 30 segundos
                    if (!string.IsNullOrEmpty(localPath) && MediaDecoder.IsVideo(localPath))
                    {
                        double duration = await MediaDecoder.GetMediaDurationSecondsAsync(localPath);
                        if (duration > 30.5 && parentWindow != null)
                        {
                            var dialog = new VideoDurationWarningDialog(file.Name, duration, vm.SelectedLanguage);
                            bool proceed = await dialog.ShowDialog<bool>(parentWindow);
                            if (!proceed)
                            {
                                continue; // Usuário optou por não importar este vídeo
                            }
                        }
                    }

                    vm.AddImportedPet(name, localPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao selecionar arquivo: {ex.Message}");
        }
    }

    // Verde: Minimiza
    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        if (window != null)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    // Amarelo: Maximiza / Restaura
    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        if (window != null)
        {
            ToggleMaximize(window);
        }
    }

    // Vermelho: Fecha a aplicação
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        GetParentWindow()?.Close();
    }

    private void ToggleMaximize(Window window)
    {
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
