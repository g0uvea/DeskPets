using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeskPets.Core.Media;
using DeskPets.Core.Models;
using DeskPets.Effects;
using SkiaSharp;

namespace DeskPets.Ui.Views;

public partial class ChromaKeyEditorDialog : Window
{
    private readonly SKBitmap? _originalBitmap;
    private readonly ChromaKeyConfig _config;
    private bool _isUpdatingUi = false;

    // Variáveis para arrastar a janela (multiplataforma)
    private PixelPoint _dragStartWindowPos;
    private Point _dragStartPointerPos;
    private bool _isManualDragging = false;

    public ChromaKeyEditorDialog()
    {
        InitializeComponent();
        _config = new ChromaKeyConfig();
    }

    public ChromaKeyEditorDialog(byte[]? sourceFrameBytes, string? filePath, ChromaKeyConfig? initialConfig, string language = "Português") : this()
    {
        if (initialConfig != null)
        {
            _config = new ChromaKeyConfig
            {
                IsEnabled = initialConfig.IsEnabled,
                KeyColorHex = initialConfig.KeyColorHex,
                Similarity = initialConfig.Similarity,
                Smoothness = initialConfig.Smoothness
            };
        }
        else
        {
            _config = new ChromaKeyConfig
            {
                IsEnabled = true,
                KeyColorHex = "#00FF00",
                Similarity = 35.0,
                Smoothness = 15.0
            };
        }

        // Carrega bitmap original
        if (sourceFrameBytes != null && sourceFrameBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(sourceFrameBytes);
                _originalBitmap = SKBitmap.Decode(ms);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChromaKeyEditor] Erro ao decodificar frame: {ex.Message}");
            }
        }

        if (_originalBitmap == null && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                byte[]? thumb = MediaDecoder.ExtractThumbnailBytesAsync(filePath).GetAwaiter().GetResult();
                if (thumb != null)
                {
                    using var ms = new MemoryStream(thumb);
                    _originalBitmap = SKBitmap.Decode(ms);
                }
                else
                {
                    _originalBitmap = SKBitmap.Decode(filePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChromaKeyEditor] Erro ao carregar arquivo de {filePath}: {ex.Message}");
            }
        }

        ApplyLocalization(language);
        InitializeValues();
        UpdatePreview();
    }

    private void ApplyLocalization(string language)
    {
        if (language == "English")
        {
            TitleText.Text = "Chroma Key (Transparent Background)";
            SubtitleText.Text = "Click on the image with the eyedropper to remove background";
            EyedropperHintText.Text = "🎯 Click to sample color";
            ColorLabelText.Text = "Selected Key Color";
            EnableChromaCheckBox.Content = "Enable";
            ToleranceLabel.Text = "Color Tolerance";
            SmoothnessLabel.Text = "Edge Smoothness";
            DisableBtnText.Text = "Disable Effect";
            ResetBtnText.Text = "↺ Reset";
            CancelBtnText.Text = "Cancel";
            SaveBtnText.Text = "Save & Apply";
        }
        else if (language == "日本語")
        {
            TitleText.Text = "クロマキー (背景透過)";
            SubtitleText.Text = "スポイトで画像をクリックして背景色を除去";
            EyedropperHintText.Text = "🎯 クリックして色を抽出";
            ColorLabelText.Text = "選択された透過色";
            EnableChromaCheckBox.Content = "有効";
            ToleranceLabel.Text = "許容範囲 (Tolerance)";
            SmoothnessLabel.Text = "境界のスムージング";
            DisableBtnText.Text = "エフェクトを無効化";
            ResetBtnText.Text = "↺ リセット";
            CancelBtnText.Text = "キャンセル";
            SaveBtnText.Text = "保存して適用";
        }
        else
        {
            DisableBtnText.Text = "Desativar Efeito";
            ResetBtnText.Text = "↺ Resetar";
            CancelBtnText.Text = "Cancelar";
            SaveBtnText.Text = "Salvar e Aplicar";
        }
    }

    private void InitializeValues()
    {
        _isUpdatingUi = true;

        EnableChromaCheckBox.IsChecked = _config.IsEnabled;
        ToleranceSlider.Value = Math.Clamp(_config.Similarity, 1, 100);
        SmoothnessSlider.Value = Math.Clamp(_config.Smoothness, 0, 50);

        ToleranceValueText.Text = $"{(int)ToleranceSlider.Value}%";
        SmoothnessValueText.Text = $"{(int)SmoothnessSlider.Value}%";

        var keyColor = ChromaKeyProcessor.ParseColor(_config.KeyColorHex, new SKColor(0, 255, 0));
        UpdateColorDisplay(keyColor);

        _isUpdatingUi = false;
    }

    private void UpdateColorDisplay(SKColor color)
    {
        HexColorText.Text = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        ColorSampleCircle.Background = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
    }

    private void SampleColorAtPointer(PointerEventArgs e)
    {
        if (_originalBitmap == null) return;

        var pos = e.GetPosition(PreviewImage);
        double boundsW = PreviewImage.Bounds.Width;
        double boundsH = PreviewImage.Bounds.Height;
        if (boundsW <= 0 || boundsH <= 0) return;

        double scaleX = boundsW / _originalBitmap.Width;
        double scaleY = boundsH / _originalBitmap.Height;
        double scale = Math.Min(scaleX, scaleY);

        double displayedW = _originalBitmap.Width * scale;
        double displayedH = _originalBitmap.Height * scale;
        double offsetX = (boundsW - displayedW) / 2.0;
        double offsetY = (boundsH - displayedH) / 2.0;

        double clickX = pos.X - offsetX;
        double clickY = pos.Y - offsetY;

        if (clickX >= 0 && clickX < displayedW && clickY >= 0 && clickY < displayedH)
        {
            int bmpX = (int)(clickX / scale);
            int bmpY = (int)(clickY / scale);
            var color = ChromaKeyProcessor.SampleColor(_originalBitmap, bmpX, bmpY);
            _config.KeyColorHex = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
            _config.IsEnabled = true;
            EnableChromaCheckBox.IsChecked = true;
            UpdateColorDisplay(color);
            UpdatePreview();
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(PreviewImage).Properties.IsLeftButtonPressed)
        {
            SampleColorAtPointer(e);
            e.Handled = true; // Impede que o clique para capturar cor inicie o arrasto da janela
        }
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(PreviewImage).Properties.IsLeftButtonPressed)
        {
            SampleColorAtPointer(e);
            e.Handled = true;
        }
    }

    // === Arrastar Janela do Diálogo pela Tela ===
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            try
            {
                BeginMoveDrag(e);
            }
            catch
            {
                _isManualDragging = true;
                _dragStartWindowPos = Position;
                _dragStartPointerPos = e.GetPosition(this);
            }
        }
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isManualDragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var currentPos = e.GetPosition(this);
            var deltaX = (int)(currentPos.X - _dragStartPointerPos.X);
            var deltaY = (int)(currentPos.Y - _dragStartPointerPos.Y);
            Position = new PixelPoint(_dragStartWindowPos.X + deltaX, _dragStartWindowPos.Y + deltaY);
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isManualDragging = false;
    }

    private void OnEnableChromaCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _config.IsEnabled = EnableChromaCheckBox.IsChecked == true;
        UpdatePreview();
    }

    private void OnToleranceSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _config.Similarity = e.NewValue;
        ToleranceValueText.Text = $"{(int)e.NewValue}%";
        UpdatePreview();
    }

    private void OnSmoothnessSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _config.Smoothness = e.NewValue;
        SmoothnessValueText.Text = $"{(int)e.NewValue}%";
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_originalBitmap == null) return;

        try
        {
            if (_config.IsEnabled)
            {
                var keyColor = ChromaKeyProcessor.ParseColor(_config.KeyColorHex, new SKColor(0, 255, 0));
                using var keyed = ChromaKeyProcessor.ApplyChromaKey(_originalBitmap, keyColor, _config.Similarity, _config.Smoothness);
                using var img = SKImage.FromBitmap(keyed);
                using var data = img.Encode(SKEncodedImageFormat.Png, 85);
                using var ms = new MemoryStream(data.ToArray());
                PreviewImage.Source = new Bitmap(ms);
            }
            else
            {
                using var img = SKImage.FromBitmap(_originalBitmap);
                using var data = img.Encode(SKEncodedImageFormat.Png, 85);
                using var ms = new MemoryStream(data.ToArray());
                PreviewImage.Source = new Bitmap(ms);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChromaKeyEditor] Erro ao renderizar preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Reseta para cor Verde Lime (#00FF00 padrão de chroma key), 35% de tolerância e 15% de suavidade.
    /// </summary>
    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _config.KeyColorHex = "#00FF00";
        _config.Similarity = 35.0;
        _config.Smoothness = 15.0;
        _config.IsEnabled = true;

        _isUpdatingUi = true;
        EnableChromaCheckBox.IsChecked = true;
        ToleranceSlider.Value = 35;
        SmoothnessSlider.Value = 15;
        ToleranceValueText.Text = "35%";
        SmoothnessValueText.Text = "15%";

        var limeColor = new SKColor(0, 255, 0);
        UpdateColorDisplay(limeColor);
        _isUpdatingUi = false;

        UpdatePreview();
    }

    private void OnDisableClick(object? sender, RoutedEventArgs e)
    {
        _config.IsEnabled = false;
        Close(_config);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _config.IsEnabled = EnableChromaCheckBox.IsChecked == true;
        Close(_config);
    }

    protected override void OnClosed(EventArgs e)
    {
        _originalBitmap?.Dispose();
        base.OnClosed(e);
    }
}
