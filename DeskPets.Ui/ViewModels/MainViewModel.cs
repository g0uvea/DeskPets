using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskPets.Ui.Models;
using DeskPets.Ui.Services;

namespace DeskPets.Ui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly int[] FpsStops = { 0, 15, 24, 30, 60 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalSelected))]
    [NotifyPropertyChangedFor(nameof(IsShopSelected))]
    [NotifyPropertyChangedFor(nameof(IsOptionsSelected))]
    private string _selectedTab = "LOCAL";

    public bool IsLocalSelected => SelectedTab == "LOCAL";
    public bool IsShopSelected => SelectedTab == "SHOP";
    public bool IsOptionsSelected => SelectedTab == "OPTIONS";

    [ObservableProperty]
    private ObservableCollection<AssetItemViewModel> _assets = new();

    [ObservableProperty]
    private AssetItemViewModel? _selectedAsset;

    // === Configurações dos Pets ===

    [ObservableProperty]
    private int _fps = 60;

    [ObservableProperty]
    private int _fpsSliderIndex = 4; // index 4 = 60 FPS

    [ObservableProperty]
    private string _fpsInputText = "60";

    [ObservableProperty]
    private int _petScale = 100;

    [ObservableProperty]
    private bool _isAlwaysOnTop = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPortugueseSelected))]
    [NotifyPropertyChangedFor(nameof(IsEnglishSelected))]
    [NotifyPropertyChangedFor(nameof(TabLocalText))]
    [NotifyPropertyChangedFor(nameof(TabShopText))]
    [NotifyPropertyChangedFor(nameof(TabOptionsText))]
    [NotifyPropertyChangedFor(nameof(FpsTitle))]
    [NotifyPropertyChangedFor(nameof(FpsSubtitle))]
    [NotifyPropertyChangedFor(nameof(PetScaleTitle))]
    [NotifyPropertyChangedFor(nameof(PetScaleSubtitle))]
    [NotifyPropertyChangedFor(nameof(LanguageTitle))]
    [NotifyPropertyChangedFor(nameof(LanguageSubtitle))]
    [NotifyPropertyChangedFor(nameof(AlwaysOnTopTitle))]
    [NotifyPropertyChangedFor(nameof(AlwaysOnTopSubtitle))]
    [NotifyPropertyChangedFor(nameof(ShopEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(EmptyLocalMessage))]
    private string _selectedLanguage = "Português";

    public bool IsPortugueseSelected => SelectedLanguage == "Português";
    public bool IsEnglishSelected => SelectedLanguage == "English";

    // Textos Dinâmicos de Localização
    public string TabLocalText => SelectedLanguage == "Português" ? "LOCAL" : "LOCAL";
    public string TabShopText => SelectedLanguage == "Português" ? "LOJA" : "SHOP";
    public string TabOptionsText => SelectedLanguage == "Português" ? "OPÇÕES" : "OPTIONS";

    public string FpsTitle => SelectedLanguage == "Português" ? "Taxa de Quadros (FPS)" : "Frame Rate (FPS)";
    public string FpsSubtitle => SelectedLanguage == "Português"
        ? "Limite de 0 a 60 FPS (paradas: 0, 15, 24, 30 e 60)"
        : "Limit from 0 to 60 FPS (stops: 0, 15, 24, 30, and 60)";

    public string PetScaleTitle => SelectedLanguage == "Português" ? "Tamanho Inicial dos Pets" : "Initial Pet Size";
    public string PetScaleSubtitle => SelectedLanguage == "Português"
        ? "Escala padrão aplicada quando um pet for invocado na tela"
        : "Default scale applied when spawning a pet on screen";

    public string LanguageTitle => SelectedLanguage == "Português" ? "Idioma do Aplicativo" : "Application Language";
    public string LanguageSubtitle => SelectedLanguage == "Português"
        ? "Selecione o idioma da interface"
        : "Select the interface language";

    public string AlwaysOnTopTitle => SelectedLanguage == "Português" ? "Sempre no Topo" : "Always on Top";
    public string AlwaysOnTopSubtitle => SelectedLanguage == "Português"
        ? "Manter janela e pets sobrepostos a outros aplicativos"
        : "Keep window and pets floating above other applications";

    public string ShopEmptyMessage => SelectedLanguage == "Português"
        ? "Novos pets, skins e animações estarão disponíveis aqui em breve."
        : "New pets, skins and animations will be available here soon.";

    public string EmptyLocalMessage => SelectedLanguage == "Português"
        ? "Nenhum pet adicionado ainda.\nClique no botão azul no topo para importar seus pets!"
        : "No pets added yet.\nClick the blue button at the top to import your pets!";

    public MainViewModel()
    {
        // Carrega configurações persistidas
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = SettingsService.Load();
        SelectedLanguage = settings.Language ?? "Português";
        PetScale = Math.Clamp(settings.PetScale, 50, 200);
        IsAlwaysOnTop = settings.IsAlwaysOnTop;
        Fps = Math.Clamp(settings.Fps, 0, 60);
        FpsInputText = Fps.ToString();

        // Encontra o stop mais próximo no slider
        FpsSliderIndex = FindClosestStopIndex(Fps);

        // Carrega pets persistidos (se houver)
        Assets.Clear();
        if (settings.ImportedPets != null && settings.ImportedPets.Count > 0)
        {
            foreach (var pet in settings.ImportedPets)
            {
                Assets.Add(new AssetItemViewModel(pet.Id, pet.Name, pet.FilePath));
            }
        }
    }

    public void SaveSettings()
    {
        var settings = new AppSettings
        {
            Fps = Fps,
            PetScale = PetScale,
            Language = SelectedLanguage,
            IsAlwaysOnTop = IsAlwaysOnTop,
            ImportedPets = Assets.Select(a => new PetAssetModel
            {
                Id = a.Id,
                Name = a.Name,
                FilePath = a.FilePath
            }).ToList()
        };

        SettingsService.Save(settings);
    }

    // Mudança de FPS via Slider (índice 0..4)
    partial void OnFpsSliderIndexChanged(int value)
    {
        if (value >= 0 && value < FpsStops.Length)
        {
            int targetFps = FpsStops[value];
            if (Fps != targetFps)
            {
                Fps = targetFps;
                FpsInputText = targetFps.ToString();
                SaveSettings();
            }
        }
    }

    // Mudança de FPS via Input Text
    partial void OnFpsInputTextChanged(string value)
    {
        if (int.TryParse(value, out int parsed))
        {
            int clamped = Math.Clamp(parsed, 0, 60);
            if (Fps != clamped)
            {
                Fps = clamped;
                int stopIndex = FindClosestStopIndex(clamped);
                if (FpsSliderIndex != stopIndex && FpsStops[stopIndex] == clamped)
                {
                    FpsSliderIndex = stopIndex;
                }
                SaveSettings();
            }
        }
    }

    partial void OnFpsChanged(int value)
    {
        SaveSettings();
    }

    partial void OnPetScaleChanged(int value)
    {
        SaveSettings();
    }

    partial void OnIsAlwaysOnTopChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        SaveSettings();
    }

    private static int FindClosestStopIndex(int fps)
    {
        int closestIndex = 0;
        int minDiff = int.MaxValue;
        for (int i = 0; i < FpsStops.Length; i++)
        {
            int diff = Math.Abs(FpsStops[i] - fps);
            if (diff < minDiff)
            {
                minDiff = diff;
                closestIndex = i;
            }
        }
        return closestIndex;
    }

    [RelayCommand]
    private void SetFpsPreset(int fps)
    {
        Fps = Math.Clamp(fps, 0, 60);
        FpsInputText = Fps.ToString();
        FpsSliderIndex = FindClosestStopIndex(Fps);
        SaveSettings();
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        SelectedTab = tab;
    }

    [RelayCommand]
    private void SelectLanguage(string language)
    {
        SelectedLanguage = language;
    }

    [RelayCommand]
    private void SelectAsset(AssetItemViewModel asset)
    {
        foreach (var a in Assets)
        {
            a.IsSelected = false;
        }

        asset.IsSelected = true;
        SelectedAsset = asset;
    }

    public void AddImportedPet(string name, string? filePath)
    {
        int nextId = Assets.Count > 0 ? Assets.Max(a => a.Id) + 1 : 1;
        var newPet = new AssetItemViewModel(nextId, name, filePath);

        foreach (var a in Assets)
        {
            a.IsSelected = false;
        }
        newPet.IsSelected = true;

        Assets.Add(newPet);
        SelectedAsset = newPet;
        SaveSettings();
    }
}
