using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DeskPets.Core.Media;
using DeskPets.Ui.ViewModels;
using DeskPets.Ui.Views;

namespace DeskPets.Ui.Helpers;

public static class PetImportHelper
{
    public static async Task ImportPetsAsync(TopLevel? topLevel, Window? parentWindow, MainViewModel? vm)
    {
        if (topLevel == null || vm == null) return;

        try
        {
            string pickerTitle = vm.SelectedLanguage switch
            {
                "English" => "Select Pets / Media Files",
                "日本語" => "ペット / メディアファイルを選択",
                _ => "Selecionar Pets / Assets"
            };

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = pickerTitle,
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(vm.SelectedLanguage switch
                    {
                        "English" => "All Supported Formats",
                        "日本語" => "サポートされているすべての形式",
                        _ => "Todos os Formatos Suportados"
                    })
                    {
                        Patterns = new[] { "*.png", "*.gif", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.mp4", "*.webm", "*.mov", "*.av1", "*.mkv", "*.avi" }
                    },
                    new FilePickerFileType(vm.SelectedLanguage switch
                    {
                        "English" => "Images and Animations (*.png, *.gif, *.webp, *.jpg)",
                        "日本語" => "画像およびアニメーション (*.png, *.gif, *.webp, *.jpg)",
                        _ => "Imagens e Animações (*.png, *.gif, *.webp, *.jpg)"
                    })
                    {
                        Patterns = new[] { "*.png", "*.gif", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                    },
                    new FilePickerFileType(vm.SelectedLanguage switch
                    {
                        "English" => "Videos (*.mp4, *.webm, *.mov, *.av1)",
                        "日本語" => "動画 (*.mp4, *.webm, *.mov, *.av1)",
                        _ => "Vídeos (*.mp4, *.webm, *.mov, *.av1)"
                    })
                    {
                        Patterns = new[] { "*.mp4", "*.webm", "*.mov", "*.av1", "*.mkv", "*.avi" }
                    },
                    FilePickerFileTypes.All
                }
            });

            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file.Name);
                    string? localPath = file.TryGetLocalPath();

                    // Se for vídeo, verifica a duração e avisa caso exceda 30 segundos
                    if (!string.IsNullOrEmpty(localPath) && MediaDecoder.IsVideo(localPath))
                    {
                        double duration = await MediaDecoder.GetMediaDurationSecondsAsync(localPath);
                        if (duration > 30.5 && parentWindow != null)
                        {
                            var dialog = new VideoDurationWarningDialog(file.Name, duration, vm.SelectedLanguage);
                            bool proceed = await dialog.ShowDialog<bool>(parentWindow);
                            if (!proceed)
                            {
                                continue; // Usuário optou por não importar este vídeo
                            }
                        }
                    }

                    vm.AddImportedPet(name, localPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PetImportHelper] Erro ao selecionar arquivo: {ex.Message}");
        }
    }
}
