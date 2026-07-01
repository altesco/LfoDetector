using System;
using Compunet.YoloSharp;
using System.IO;
using System.Net.Http;
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

    public static readonly HttpClient Client = new HttpClient();

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
            SuggestedFileType = new FilePickerFileType("Файлы .onnx и .pt")
            {
                Patterns = ["*.onnx", "*.pt"] 
            }
        };
        var models = await storageProvider.OpenFilePickerAsync(options);
        if (models.Count <= 0)
            return;

        var path = models[0].Path.LocalPath;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".onnx")
        {
            ModelPath = path;
            return;
        }

        try
        {
            var url = $"http://127.0.0.1:5050/convert_model?file_path={path}";
            var response = await Client.PostAsync(url, null);
            if (response.IsSuccessStatusCode)
                ModelPath = await response.Content.ReadAsStringAsync();
        }
        catch(HttpRequestException ex)
        {
            Console.WriteLine(ex.Message);
        }
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
