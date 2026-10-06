using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskPets.Core.Media;

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
    private string _mediaType = "Image";

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpawned))]
    private int _activeInstanceCount = 0;

    public bool IsSpawned => ActiveInstanceCount > 0;

    [ObservableProperty]
    private bool _isCardHighlighted = false;

    public AssetItemViewModel(int id, string name = "Asset", string? filePath = null)
    {
        _id = id;
        _name = name;
        _filePath = filePath;

        if (!string.IsNullOrEmpty(filePath))
        {
            _mediaType = MediaDecoder.DetectMediaType(filePath);
            _ = LoadThumbnailAsync();
        }
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            return;

        try
        {
            byte[]? thumbBytes = await MediaDecoder.ExtractThumbnailBytesAsync(FilePath, 140, 140);
            if (thumbBytes != null && thumbBytes.Length > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(thumbBytes);
                        Thumbnail = new Bitmap(ms);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AssetItemViewModel] Erro ao instanciar Bitmap: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetItemViewModel] Erro ao carregar thumbnail para {FilePath}: {ex.Message}");
        }
    }
}
