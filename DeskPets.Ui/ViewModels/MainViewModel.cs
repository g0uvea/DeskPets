using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
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
    private int _petScale = 100;

    [ObservableProperty]
    private int _petBaseWidth = 600;

    [ObservableProperty]
    private int _petBaseHeight = 600;

    [ObservableProperty]
    private string _petBaseWidthText = "600";

    [ObservableProperty]
    private string _petBaseHeightText = "600";

    [ObservableProperty]
    private bool _isAlwaysOnTop = false;

    [ObservableProperty]
    private bool _enableAnimations = true;

    [ObservableProperty]
    private bool _allowDuplicatePets = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPortugueseSelected))]
    [NotifyPropertyChangedFor(nameof(IsEnglishSelected))]
    [NotifyPropertyChangedFor(nameof(IsJapaneseSelected))]
    [NotifyPropertyChangedFor(nameof(TabLocalText))]
    [NotifyPropertyChangedFor(nameof(TabShopText))]
    [NotifyPropertyChangedFor(nameof(TabOptionsText))]
    [NotifyPropertyChangedFor(nameof(FpsTitle))]
    [NotifyPropertyChangedFor(nameof(FpsSubtitle))]
    [NotifyPropertyChangedFor(nameof(PetScaleTitle))]
    [NotifyPropertyChangedFor(nameof(PetScaleSubtitle))]
    [NotifyPropertyChangedFor(nameof(BaseWidthTitle))]
    [NotifyPropertyChangedFor(nameof(BaseHeightTitle))]
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
    [NotifyPropertyChangedFor(nameof(AddPetMenuText))]
    private string _selectedLanguage = "Português";

    public bool IsPortugueseSelected => SelectedLanguage == "Português";
    public bool IsEnglishSelected => SelectedLanguage == "English";
    public bool IsJapaneseSelected => SelectedLanguage == "日本語";

    // Textos Dinâmicos de Localização
    public string TabLocalText => SelectedLanguage switch { "English" => "LOCAL", "日本語" => "ローカル", _ => "LOCAL" };
    public string TabShopText => SelectedLanguage switch { "English" => "SHOP", "日本語" => "ショップ", _ => "LOJA" };
    public string TabOptionsText => SelectedLanguage switch { "English" => "OPTIONS", "日本語" => "設定", _ => "OPÇÕES" };

    public string FpsTitle => SelectedLanguage switch { "English" => "Frame Rate (FPS)", "日本語" => "フレームレート (FPS)", _ => "Taxa de Quadros (FPS)" };
    public string FpsSubtitle => SelectedLanguage switch
    {
        "English" => "Limit from 0 to 60 FPS (stops: 0, 15, 24, 30, and 60)",
        "日本語" => "0〜60 FPSの上限 (停止位置: 0, 15, 24, 30, 60)",
        _ => "Limite de 0 a 60 FPS (paradas: 0, 15, 24, 30 e 60)"
    };

    public string PetScaleTitle => SelectedLanguage switch { "English" => "Initial Pet Size & Scale", "日本語" => "初期サイズと拡大率", _ => "Tamanho e Escala Inicial" };
    public string PetScaleSubtitle => SelectedLanguage switch
    {
        "English" => "Default size and scale applied when spawning a pet on screen",
        "日本語" => "画面にペットを召喚する際のデフォルトサイズと拡大率",
        _ => "Dimensões base e escala padrão aplicadas ao invocar o pet na tela"
    };

    public string BaseWidthTitle => SelectedLanguage switch { "English" => "Base Width (px)", "日本語" => "基本の幅 (px)", _ => "Largura Base (px)" };
    public string BaseHeightTitle => SelectedLanguage switch { "English" => "Base Height (px)", "日本語" => "基本の高さ (px)", _ => "Altura Base (px)" };

    public string LanguageTitle => SelectedLanguage switch { "English" => "Application Language", "日本語" => "アプリの言語", _ => "Idioma do Aplicativo" };
    public string LanguageSubtitle => SelectedLanguage switch
    {
        "English" => "Select the interface language",
        "日本語" => "インターフェースの言語を選択",
        _ => "Selecione o idioma da interface"
    };

    public string AlwaysOnTopTitle => SelectedLanguage switch { "English" => "Pets Always on Top", "日本語" => "常に最前面に表示", _ => "Pets Sempre no Topo" };
    public string AlwaysOnTopSubtitle => SelectedLanguage switch
    {
        "English" => "Keep pets floating above other applications on the desktop",
        "日本語" => "デスクトップ上の他のウィンドウの手前にペットを表示",
        _ => "Manter os pets sobrepostos a outros aplicativos na área de trabalho"
    };

    public string AnimationsTitle => SelectedLanguage switch { "English" => "Launcher Animations", "日本語" => "ランチャーのアニメーション", _ => "Animações no Launcher" };
    public string AnimationsSubtitle => SelectedLanguage switch
    {
        "English" => "Enable smooth transitions and library preview in launcher",
        "日本語" => "ランチャー内のスムーズな遷移とライブラリのプレビューを有効化",
        _ => "Ativar transições suaves e prévia animada no launcher"
    };

    public string AllowDuplicatePetsTitle => SelectedLanguage switch
    {
        "English" => "Summon Duplicate Pets",
        "日本語" => "重複ペットの召喚",
        _ => "Sumonar Pets Duplicados"
    };
    public string AllowDuplicatePetsSubtitle => SelectedLanguage switch
    {
        "English" => "Allow spawning multiple copies of the same pet on screen at once",
        "日本語" => "同じペットを画面上に複数召喚することを許可",
        _ => "Permite criar múltiplas cópias do mesmo pet simultaneamente na tela"
    };

    public string ShopEmptyMessage => SelectedLanguage switch
    {
        "English" => "New pets, skins and animations will be available here soon.",
        "日本語" => "新しいペット、スキン、アニメーションが近日公開予定です。",
        _ => "Novos pets, skins e animações estarão disponíveis aqui em breve."
    };

    public string EmptyLocalMessage => SelectedLanguage switch
    {
        "English" => "No pets added yet.\nRight-click or click the blue button (+) above to import your pets!",
        "日本語" => "ペットがまだ追加されていません。\n右クリックまたは上の青いボタン（＋）からペットをインポートしてください！",
        _ => "Nenhum pet adicionado ainda.\nClique com o botão direito ou no botão azul (+) acima para importar seus pets!"
    };

    public string SpawnPetMenuText => SelectedLanguage switch { "English" => "Spawn Pet on Screen", "日本語" => "画面にペットを召喚", _ => "Spawnar Pet na Tela" };
    public string RemovePetMenuText => SelectedLanguage switch { "English" => "Remove from Library", "日本語" => "ライブラリから削除", _ => "Remover da Biblioteca" };
    public string AddPetMenuText => SelectedLanguage switch { "English" => "Import Pets...", "日本語" => "ペットをインポート...", _ => "Importar Pets..." };

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
        PetBaseWidth = Math.Clamp(settings.PetBaseWidth, 200, 1000);
        PetBaseHeight = Math.Clamp(settings.PetBaseHeight, 200, 1000);
        PetBaseWidthText = PetBaseWidth.ToString();
        PetBaseHeightText = PetBaseHeight.ToString();
        IsAlwaysOnTop = settings.IsAlwaysOnTop;
        EnableAnimations = settings.EnableAnimations;
        AllowDuplicatePets = settings.AllowDuplicatePets;
        Fps = Math.Clamp(settings.Fps, 0, 60);
        FpsSliderIndex = FindClosestStopIndex(Fps);

        // 2. Carrega biblioteca de pets do pets.json
        var storedPets = PetStorageService.LoadPets();
        Assets.Clear();
        if (storedPets != null && storedPets.Count > 0)
        {
            foreach (var pet in storedPets)
            {
                var item = new AssetItemViewModel(pet.Id, pet.Name, pet.FilePath, pet.ChromaKey)
                {
                    CreatedAt = pet.CreatedAt,
                    Scale = pet.Scale,
                    Fps = pet.Fps,
                    FlipHorizontal = pet.FlipHorizontal,
                    FlipVertical = pet.FlipVertical,
                    DesktopInteractionEnabled = pet.DesktopInteractionEnabled,
                    DesktopInteractionType = pet.DesktopInteractionType
                };
                Assets.Add(item);
            }
        }

        UpdateCardHighlights();
        UpdateLibraryAnimationTimer();
    }

    public void SaveSettings()
    {
        var settings = new AppSettings
        {
            Fps = Fps,
            PetScale = PetScale,
            PetBaseWidth = PetBaseWidth,
            PetBaseHeight = PetBaseHeight,
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
            MediaType = a.MediaType,
            CreatedAt = a.CreatedAt,
            ChromaKey = a.ChromaKey,
            Scale = a.Scale,
            Fps = a.Fps,
            FlipHorizontal = a.FlipHorizontal,
            FlipVertical = a.FlipVertical,
            DesktopInteractionEnabled = a.DesktopInteractionEnabled,
            DesktopInteractionType = a.DesktopInteractionType
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
                SaveSettings();
            }
        }
    }

    partial void OnPetBaseWidthTextChanged(string value)
    {
        if (int.TryParse(value, out int parsed))
        {
            int clamped = Math.Clamp(parsed, 200, 1000);
            if (PetBaseWidth != clamped)
            {
                PetBaseWidth = clamped;
                SaveSettings();
            }
        }
    }

    partial void OnPetBaseHeightTextChanged(string value)
    {
        if (int.TryParse(value, out int parsed))
        {
            int clamped = Math.Clamp(parsed, 200, 1000);
            if (PetBaseHeight != clamped)
            {
                PetBaseHeight = clamped;
                SaveSettings();
            }
        }
    }

    partial void OnFpsChanged(int value)
    {
        UpdateLibraryAnimationTimer();
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

    partial void OnSelectedTabChanged(string value)
    {
        UpdateLibraryAnimationTimer();
    }

    partial void OnEnableAnimationsChanged(bool value)
    {
        UpdateLibraryAnimationTimer();
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
        UpdateLibraryAnimationTimer();
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
        UpdateLibraryAnimationTimer();
    }

    // === Loop Centralizado e Econômico para Pré-visualização na Biblioteca (respeita o FPS das configurações) ===
    private DispatcherTimer? _libraryAnimationTimer;
    private readonly System.Diagnostics.Stopwatch _libraryStopwatch = new();

    public void UpdateLibraryAnimationTimer()
    {
        if (EnableAnimations && IsLocalSelected && Fps > 0 && Assets.Any(a => a.MediaType != "Image"))
        {
            int safeFps = Math.Clamp(Fps, 1, 60);
            var targetInterval = TimeSpan.FromSeconds(1.0 / safeFps);

            if (_libraryAnimationTimer == null)
            {
                _libraryAnimationTimer = new DispatcherTimer(DispatcherPriority.Render);
                _libraryAnimationTimer.Tick += OnLibraryAnimationTick;
            }

            _libraryAnimationTimer.Interval = targetInterval;

            if (!_libraryAnimationTimer.IsEnabled)
            {
                _libraryStopwatch.Restart();
                _libraryAnimationTimer.Start();
            }
        }
        else
        {
            _libraryAnimationTimer?.Stop();
            _libraryStopwatch.Reset();
        }
    }

    private void OnLibraryAnimationTick(object? sender, EventArgs e)
    {
        if (!IsLocalSelected || !EnableAnimations || Fps <= 0)
        {
            _libraryAnimationTimer?.Stop();
            _libraryStopwatch.Reset();
            return;
        }

        long elapsedMs = _libraryStopwatch.ElapsedMilliseconds;

        foreach (var asset in Assets)
        {
            if (asset.MediaType != "Image")
            {
                asset.AdvancePreviewFrame(elapsedMs);
            }
        }
    }
}
