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
        if (DataContext is MainViewModel vm)
        {
            await DeskPets.Ui.Helpers.PetImportHelper.ImportPetsAsync(topLevel, GetParentWindow(), vm);
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
