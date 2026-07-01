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
using SixLabors.ImageSharp.PixelFormats;
using OpenCvSharp;
using System.Collections.Generic;

namespace LfoDetector.ViewModels;

public partial class MainVM : ViewModelBase
{
    [ObservableProperty] private string _result = string.Empty;
    [ObservableProperty] private Bitmap? _activeImage;
    
    public string? ImagePath { get; set; }
    public string? ModelPath { get; set; }

    public readonly ConfigurationVM Config = new();
    public static readonly HttpClient Client = new ();

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
            Title = "Выберите изображение или видео",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Все медиафайлы") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.mp4", "*.avi", "*.mkv", "*.mov"] }
            }
        };

        var files = await storageProvider.OpenFilePickerAsync(options);
        if (files.Count <= 0)
            return;

        string filePath = files[0].Path.LocalPath;
        string extension = Path.GetExtension(filePath).ToLower();

        var imageExtensions = new List<string> { ".jpg", ".jpeg", ".png" };
        ImagePath = filePath;
        if (imageExtensions.Contains(extension))
        {
            // --- ЛОГИКА ДЛЯ КАРТИНКИ ---
            
            await using var stream = await files[0].OpenReadAsync();
            ActiveImage = new Bitmap(stream);
        }
        else
        {
            //мне чисто для себя нужно было запустить видео тут
            //поменяешь как надо
            _ = Task.Run(() => PlayVideoInSeparateWindow(filePath));
        }
    }

    //Метод для запуска видео в отдельном окне
    private void PlayVideoInSeparateWindow(string videoPath)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened()) return;

        using var frame = new Mat();

        // Вместо Cv2.NamedWindow крутим бесконечный цикл чтения кадров
        while (capture.Read(frame) && !frame.Empty())
        {
            // Конвертируем BGR (OpenCV) в RGB (ImageSharp/Avalonia)
            using var rgbFrame = new Mat();
            Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.BGR2RGB);

            // Переводим в байты и создаем Bitmap для Avalonia
            byte[] imageBytes = rgbFrame.ToBytes(".jpg");
            using var ms = new MemoryStream(imageBytes);
            var bitmap = new Bitmap(ms);

            // Отправляем кадр прямиком в UI-поток на твой Image контроль!
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ActiveImage = bitmap; // Свойство из твоей MainVM
            });

            // Задержка ~30 FPS, чтобы видео не летело на первой космической скорости
            Task.Delay(33).Wait();
        }
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
        if (ModelPath is null || ImagePath is null)
        {
            Result = "Сначала выберите модель и медиафайл!";
            return;
        }

        var options = new YoloPredictorOptions()
        {
            Configuration = new()
            {
                Confidence = (float)Config.Confidence,
                KeepAspectRatio = true,
                SuppressParallelInference = Config.SuppressParallelInference,
                IoU = (float)Config.IoU,
                ApplyAutoOrient = Config.ApplyAutoOrient
            }
        };

        // Проверяем, что выбрано: картинка или видео
        string extension = Path.GetExtension(ImagePath).ToLower();
        var imageExtensions = new List<string> { ".jpg", ".jpeg", ".png" };

        //для картинки
        if (imageExtensions.Contains(extension))
        {
            // --- ОБРАБОТКА ОДНОЧНОЙ КАРТИНКИ ---
            using var predictor = new YoloPredictor(ModelPath, options);
            using var image = await Image.LoadAsync<Rgb24>(ImagePath);

            var result = await predictor.DetectAsync(image);
            using var plotted = await result.PlotImageAsync(image);

            using var ms = new MemoryStream();
            await plotted.SaveAsPngAsync(ms);
            ms.Position = 0;

            ActiveImage = new Bitmap(ms);
           
        }
        //для видоса
        //Но он долго робит прям оч и не очень хорошо
        else
        {

            await Task.Run(async () =>
            {
                using var predictor = new YoloPredictor(ModelPath, options);
                using var capture = new VideoCapture(ImagePath);
                using var matFrame = new Mat();

                if (!capture.IsOpened())
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        Result = "Ошибка: Не удалось открыть видеофайл.";
                    });
                    return;
                }

                while (capture.Read(matFrame) && !matFrame.Empty())
                {
                    using var rgbFrame = new Mat();
                    Cv2.CvtColor(matFrame, rgbFrame, ColorConversionCodes.BGR2RGB);

                    using var resizedFrame = new Mat();

                    var newSize = new OpenCvSharp.Size(640, 480);
                    Cv2.Resize(rgbFrame, resizedFrame, newSize, 0, 0, InterpolationFlags.Linear);

                    byte[] imageBytes = resizedFrame.ToBytes(".jpg");
                    using var image = Image.Load<Rgb24>(imageBytes);


                    var result = await predictor.DetectAsync(image);
                    using var plotted = await result.PlotImageAsync(image);

                    using var ms = new MemoryStream();
                    await plotted.SaveAsJpegAsync(ms); 
                    ms.Position = 0;

  
                    var bitmap = new Bitmap(ms);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ActiveImage = bitmap;
                       
                    });

                    //await Task.Delay(1); // хз нужна ли задержка тут
                }

            });
        }
    }
}
