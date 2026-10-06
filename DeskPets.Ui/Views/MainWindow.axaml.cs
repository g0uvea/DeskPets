using System;
using Avalonia.Controls;
using Avalonia.Input;
using DeskPets.Ui.ViewModels;

namespace DeskPets.Ui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SpawnPetRequested -= OnSpawnPetRequested;
            vm.SpawnPetRequested += OnSpawnPetRequested;
        }
    }

    private void OnSpawnPetRequested(AssetItemViewModel pet)
    {
        if (DataContext is MainViewModel vm)
        {
            var petWindow = new PetSpawnWindow(pet, vm.Fps, vm.PetScale);
            petWindow.Show();
        }
    }

    private void OnBottomRightResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
        }
    }

    private void OnRightResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.East, e);
        }
    }

    private void OnBottomResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.South, e);
        }
    }
}