using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
                    new FilePickerFileType("Imagens e Assets")
                    {
                        Patterns = new[] { "*.png", "*.gif", "*.jpg", "*.jpeg", "*.svg", "*.webp", "*.json" }
                    },
                    FilePickerFileTypes.All
                }
            });

            if (files != null && files.Count > 0 && DataContext is MainViewModel vm)
            {
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file.Name);
                    string? localPath = file.TryGetLocalPath();
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
