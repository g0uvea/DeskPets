using CommunityToolkit.Mvvm.ComponentModel;

namespace DeskPets.Ui.ViewModels;

public partial class AssetItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _name = "Asset";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isSelected;

    public AssetItemViewModel(int id, string name = "Asset", string? filePath = null)
    {
        _id = id;
        _name = name;
        _filePath = filePath;
    }
}
