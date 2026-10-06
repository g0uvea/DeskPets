using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskPets.Core.Models;
using DeskPets.Core.Services;

namespace DeskPets.Ui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly int[] FpsStops = { 0, 15, 24, 30, 60 };

    public event Action<AssetItemViewModel>? SpawnPetRequested;

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
    [NotifyPropertyChangedFor(nameof(IsFps0Selected))]
    [NotifyPropertyChangedFor(nameof(IsFps15Selected))]
    [NotifyPropertyChangedFor(nameof(IsFps24Selected))]
    [NotifyPropertyChangedFor(nameof(IsFps30Selected))]
    [NotifyPropertyChangedFor(nameof(IsFps60Selected))]
    private int _fps = 60;

    public bool IsFps0Selected => Fps == 0;
    public bool IsFps15Selected => Fps == 15;
    public bool IsFps24Selected => Fps == 24;
    public bool IsFps30Selected => Fps == 30;
    public bool IsFps60Selected => Fps == 60;

    [ObservableProperty]
    private int _fpsSliderIndex = 4; // index 4 = 60 FPS

    [ObservableProperty]
    private string _fpsInputText = "60";

    [ObservableProperty]
    private int _petScale = 100;

    [ObservableProperty]
    private bool _isAlwaysOnTop = false;

    [ObservableProperty]
    private bool _enableAnimations = true;

    [ObservableProperty]
    private bool _allowDuplicatePets = true;

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
    [NotifyPropertyChangedFor(nameof(AnimationsTitle))]
    [NotifyPropertyChangedFor(nameof(AnimationsSubtitle))]
    [NotifyPropertyChangedFor(nameof(AllowDuplicatePetsTitle))]
    [NotifyPropertyChangedFor(nameof(AllowDuplicatePetsSubtitle))]
    [NotifyPropertyChangedFor(nameof(ShopEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(EmptyLocalMessage))]
    [NotifyPropertyChangedFor(nameof(SpawnPetMenuText))]
    [NotifyPropertyChangedFor(nameof(RemovePetMenuText))]
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

    public string AnimationsTitle => SelectedLanguage == "Português" ? "Animações na Interface" : "Interface Animations";
    public string AnimationsSubtitle => SelectedLanguage == "Português"
        ? "Ativar transições suaves e efeitos visuais"
        : "Enable smooth transitions and visual effects";

    public string AllowDuplicatePetsTitle => SelectedLanguage == "Português"
        ? "Sumonar Pets Duplicados"
        : "Summon Duplicate Pets";
    public string AllowDuplicatePetsSubtitle => SelectedLanguage == "Português"
        ? "Permite criar múltiplas cópias do mesmo pet simultaneamente na tela"
        : "Allow spawning multiple copies of the same pet on screen at once";

    public string ShopEmptyMessage => SelectedLanguage == "Português"
        ? "Novos pets, skins e animações estarão disponíveis aqui em breve."
        : "New pets, skins and animations will be available here soon.";

    public string EmptyLocalMessage => SelectedLanguage == "Português"
        ? "Nenhum pet adicionado ainda.\nClique no botão azul (+) acima para importar seus pets!"
        : "No pets added yet.\nClick the blue button (+) above to import your pets!";

    public string SpawnPetMenuText => SelectedLanguage == "Português" ? "Spawnar Pet na Tela" : "Spawn Pet on Screen";
    public string RemovePetMenuText => SelectedLanguage == "Português" ? "Remover da Biblioteca" : "Remove from Library";

    public MainViewModel()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        // 1. Carrega configurações do settings.json
        var settings = SettingsService.Load();
        SelectedLanguage = settings.Language ?? "Português";
        PetScale = Math.Clamp(settings.PetScale, 50, 200);
        IsAlwaysOnTop = settings.IsAlwaysOnTop;
        EnableAnimations = settings.EnableAnimations;
        AllowDuplicatePets = settings.AllowDuplicatePets;
        Fps = Math.Clamp(settings.Fps, 0, 60);
        FpsInputText = Fps.ToString();
        FpsSliderIndex = FindClosestStopIndex(Fps);

        // 2. Carrega biblioteca de pets do pets.json
        var storedPets = PetStorageService.LoadPets();
        Assets.Clear();
        if (storedPets != null && storedPets.Count > 0)
        {
            foreach (var pet in storedPets)
            {
                Assets.Add(new AssetItemViewModel(pet.Id, pet.Name, pet.FilePath));
            }
        }

        UpdateCardHighlights();
    }

    public void SaveSettings()
    {
        var settings = new AppSettings
        {
            Fps = Fps,
            PetScale = PetScale,
            Language = SelectedLanguage,
            IsAlwaysOnTop = IsAlwaysOnTop,
            EnableAnimations = EnableAnimations,
            AllowDuplicatePets = AllowDuplicatePets
        };

        SettingsService.Save(settings);
    }

    public void SavePets()
    {
        var pets = Assets.Select(a => new PetAssetModel
        {
            Id = a.Id,
            Name = a.Name,
            FilePath = a.FilePath,
            MediaType = a.MediaType
        }).ToList();

        PetStorageService.SavePets(pets);
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

    partial void OnEnableAnimationsChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnAllowDuplicatePetsChanged(bool value)
    {
        UpdateCardHighlights();
        SaveSettings();
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        SaveSettings();
    }

    public void UpdateCardHighlights()
    {
        foreach (var asset in Assets)
        {
            if (AllowDuplicatePets)
            {
                // Se o modo de pets duplicados estiver ativo, remove a borda de foco para não prender no card
                asset.IsCardHighlighted = false;
                asset.IsSelected = false;
            }
            else
            {
                // Se duplicados estiver desativado, mantém a borda clara apenas nos pets que estão em tela
                asset.IsCardHighlighted = asset.IsSpawned;
                asset.IsSelected = asset.IsSpawned;
            }
        }
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
        SelectedAsset = asset;
        SpawnPetRequested?.Invoke(asset);
    }

    [RelayCommand]
    private void SpawnPet(AssetItemViewModel asset)
    {
        SpawnPetRequested?.Invoke(asset);
    }

    [RelayCommand]
    private void RemovePet(AssetItemViewModel asset)
    {
        Assets.Remove(asset);
        if (SelectedAsset == asset)
        {
            SelectedAsset = Assets.FirstOrDefault();
            if (SelectedAsset != null)
            {
                SelectedAsset.IsSelected = true;
            }
        }
        SavePets();
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
        SavePets();
    }
}
