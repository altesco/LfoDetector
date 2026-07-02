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
    [ObservableProperty] private string? _error;
    [ObservableProperty] private int? _currentFrame;
    
    public string? ImagePath { get; set; }
    public string? ModelPath { get; set; }

    public ConfigurationVM Config { get; set; } = new();
    public static readonly HttpClient Client = new ();

    [ObservableProperty] private bool _isSidebarOpen;

    [RelayCommand]
    private void SwitchSidebar() => IsSidebarOpen = !IsSidebarOpen;

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
            //Если врубать видео так, то оно с результатов пересекается
            //
            //_ = Task.Run(() => PlayVideoInSeparateWindow(filePath));
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

        CurrentFrame = null;

        var configuration = new YoloConfiguration
        {
            Confidence = (float)Config.Confidence,
            KeepAspectRatio = true,
            SuppressParallelInference = Config.SuppressParallelInference,
            IoU = (float)Config.IoU,
            ApplyAutoOrient = Config.ApplyAutoOrient
        };

        YoloPredictorOptions options = new YoloPredictorOptions
        {
            Configuration = configuration
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
        else
        {
            await Task.Run(async () =>
            {
                using var predictor = new YoloPredictor(ModelPath, options);
                using var capture = new VideoCapture(ImagePath);
                using var matFrame = new Mat();

                if (!capture.IsOpened())
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Result = "Ошибка: Не удалось открыть видеофайл.";
                    });
                    return;
                }

                CurrentFrame = 0;

                while (capture.Read(matFrame) && !matFrame.Empty())
                {
                    CurrentFrame++;
                    // 1. Кодируем кадр в ультрабыстрый разжатый BMP (это мгновенно)
                    // OpenCV выдает BGR, ImageSharp сам корректно распарсит его из BMP заголовка
                    byte[] imageBytes = matFrame.ToBytes(".bmp");
                    using var image = Image.Load<Rgb24>(imageBytes);

                    // 2. Отправляем кадр в оригинальном соотношении сторон.
                    // YoloSharp сам сделает правильный квадратный resize и padding, анкоры больше не упадут!
                    var result = await predictor.DetectAsync(image);
                    using var plotted = await result.PlotImageAsync(image);

                    // 3. Переводим результат работы в Bitmap для Avalonia
                    using var ms = new MemoryStream();
                    await plotted.SaveAsJpegAsync(ms);
                    ms.Position = 0;
                    var bitmap = new Bitmap(ms);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { ActiveImage = bitmap; });

                    // 4. Ограничиваем FPS (~30 кадров/сек), чтобы CPU успевал дышать
                    await Task.Delay(33);
                }
            });
        }
    }
}
