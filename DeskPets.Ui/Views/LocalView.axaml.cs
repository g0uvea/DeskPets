using Avalonia.Controls;
using Avalonia.Interactivity;
using DeskPets.Ui.Helpers;
using DeskPets.Ui.ViewModels;

namespace DeskPets.Ui.Views;

public partial class LocalView : UserControl
{
    public LocalView()
    {
        InitializeComponent();
    }

    public async void OnAddPetContextMenuClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var parentWindow = topLevel as Window;
        if (DataContext is MainViewModel vm)
        {
            await PetImportHelper.ImportPetsAsync(topLevel, parentWindow, vm);
        }
    }
}
