using Compunet.YoloSharp;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Avalonia;
using Compunet.YoloSharp.Plotting;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;

namespace LfoDetector.ViewModels;

public partial class MainVM : ViewModelBase
{
    [ObservableProperty] private string _result = string.Empty;
    [ObservableProperty] private Bitmap? _activeImage;
    
    public string? ImagePath { get; set; }
    public string? ModelPath { get; set; }

    [RelayCommand]
    private async Task LoadImage()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var storageProvider = desktop?.MainWindow?.StorageProvider;
        if (storageProvider is null)
            return;

        var options = new FilePickerOpenOptions()
        {
            AllowMultiple = false,
            SuggestedFileType = new FilePickerFileType("Изображение")
            {
                Patterns = ["*.jpg", "*.jpeg", "*.png"]
            }
        };
        var images = await storageProvider.OpenFilePickerAsync(options);
        if (images.Count <= 0)
            return;
        
        ImagePath = images[0].Path.LocalPath;
        await using var stream = await images[0].OpenReadAsync();
        ActiveImage = new Bitmap(stream);
    }

    [RelayCommand]
    private async Task LoadModel()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var storageProvider = desktop?.MainWindow?.StorageProvider;
        if (storageProvider is null)
            return;

        var options = new FilePickerOpenOptions()
        {
            AllowMultiple = false,
            SuggestedFileType = new FilePickerFileType("Файл .onnx")
            {
                Patterns = ["*.onnx"] 
                // тут можно потом сделать так чтобы еще можно было выбирать .pt
                // и при загрузке будет конвертация через сервер model_loader.py
            }
        };
        var models = await storageProvider.OpenFilePickerAsync(options);
        if (models.Count <= 0)
            return;

        ModelPath = models[0].Path.LocalPath;
    }

    [RelayCommand]
    private async Task Detect()
    {
        if (ImagePath is null || ModelPath is null)
            return;

        var options = new YoloPredictorOptions()
        {
            Configuration = new()
            {
                Confidence = 0.1f
            }
        };

        using var predictor = new YoloPredictor(ModelPath, options);
        using var image = await Image.LoadAsync(ImagePath);   
        
        var result = await predictor.DetectAsync(image);
        using var plotted = await result.PlotImageAsync(image);

        using var ms = new MemoryStream();
        await plotted.SaveAsPngAsync(ms);
        ms.Position = 0;
        ActiveImage = new Bitmap(ms);
    }
}
