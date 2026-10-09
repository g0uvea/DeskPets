using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DeskPets.Ui.Views;

public partial class VideoDurationWarningDialog : Window
{
    public VideoDurationWarningDialog()
    {
        InitializeComponent();
    }

    public VideoDurationWarningDialog(string fileName, double durationSeconds, string language = "Português") : this()
    {
        FileNameText.Text = fileName;
        DurationBadgeText.Text = $"~{(int)Math.Round(durationSeconds)}s";

        if (language == "English")
        {
            TitleText.Text = "Video Duration Notice";
            SubtitleText.Text = "30-second maximum limit";
            ExplanationText.Text = "This video exceeds the recommended duration for desktop pets. It will be trimmed to exactly 30 seconds, which may cause a visual discontinuity when the animation loops.";
            CancelBtn.Content = "Cancel";
            ProceedBtn.Content = "Proceed and Trim (30s)";
        }
        else if (language == "日本語")
        {
            TitleText.Text = "動画の再生時間に関する警告";
            SubtitleText.Text = "最大30秒の制限";
            ExplanationText.Text = "この動画はデスクトップペットの推奨再生時間を超えています。30秒にトリミングされるため、ループ再生時に不連続が生じる場合があります。";
            CancelBtn.Content = "キャンセル";
            ProceedBtn.Content = "続行してトリミング (30秒)";
        }
        else
        {
            TitleText.Text = "Aviso de Duração do Vídeo";
            SubtitleText.Text = "Limite máximo de 30 segundos";
            ExplanationText.Text = "Este vídeo ultrapassa o limite recomendado para pets na tela. Ele será cortado em exatos 30 segundos, o que poderá causar uma quebra de continuidade ao reiniciar o ciclo da animação.";
            CancelBtn.Content = "Cancelar";
            ProceedBtn.Content = "Continuar e Cortar (30s)";
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnProceedClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
