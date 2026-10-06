using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using DeskPets.Ui.ViewModels;

namespace DeskPets.Ui.Views;

public partial class MainWindow : Window
{
    private readonly List<PetSpawnWindow> _activePetWindows = new();

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

            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Atualização reativa dinâmica para todos os pets ativos na área de trabalho
        if (e.PropertyName == nameof(MainViewModel.IsAlwaysOnTop))
        {
            foreach (var petWin in _activePetWindows.ToArray())
            {
                petWin.Topmost = vm.IsAlwaysOnTop;
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.EnableAnimations))
        {
            foreach (var petWin in _activePetWindows.ToArray())
            {
                petWin.SetAnimationsEnabled(vm.EnableAnimations);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.Fps))
        {
            foreach (var petWin in _activePetWindows.ToArray())
            {
                petWin.UpdateFps(vm.Fps);
            }
        }
    }

    private void OnSpawnPetRequested(AssetItemViewModel pet)
    {
        if (DataContext is MainViewModel vm)
        {
            // Se pets duplicados estiverem desativados, impede spawn repetido e traz o existente para foco
            if (!vm.AllowDuplicatePets)
            {
                var existing = _activePetWindows.Find(w => w.Pet?.Id == pet.Id);
                if (existing != null)
                {
                    existing.Activate();
                    vm.UpdateCardHighlights();
                    return;
                }
            }

            var petWindow = new PetSpawnWindow(
                pet,
                vm.Fps,
                vm.PetScale,
                vm.IsAlwaysOnTop,
                vm.EnableAnimations);

            _activePetWindows.Add(petWindow);
            pet.ActiveInstanceCount++;
            vm.UpdateCardHighlights();

            petWindow.Closed += (s, e) =>
            {
                _activePetWindows.Remove(petWindow);
                pet.ActiveInstanceCount = Math.Max(0, pet.ActiveInstanceCount - 1);
                vm.UpdateCardHighlights();
            };

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

    protected override void OnClosed(EventArgs e)
    {
        // Fecha pets ao encerrar a aplicação principal
        foreach (var petWin in _activePetWindows.ToArray())
        {
            try { petWin.Close(); } catch { }
        }
        _activePetWindows.Clear();

        base.OnClosed(e);
    }
}