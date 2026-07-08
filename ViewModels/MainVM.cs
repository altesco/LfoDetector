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
using System.Threading;

namespace LfoDetector.ViewModels;

public partial class MainVM : ViewModelBase
{
    [ObservableProperty] private Bitmap? _activeImage;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private int? _currentFrame;
    [ObservableProperty] private bool _isVideoLoaded;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isVideoEnded = true;
    [ObservableProperty] private bool _isVideoSelected;

    public string? ImagePath
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ImageName));
            DetectCommand.NotifyCanExecuteChanged();
        }
    }

    public string? ModelPath
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ModelName));
            DetectCommand.NotifyCanExecuteChanged();
        }
    }

    public string ImageName => Path.GetFileName(ImagePath) ?? "Файл медиа";
    public string ModelName => Path.GetFileName(ModelPath) ?? "Файл модели";

    public bool CanDetect => ImagePath != null && ModelPath != null;

    [ObservableProperty] private double _confidence = 0.5;

    public static readonly HttpClient Client = new();

    private CancellationTokenSource? _stockVideoCts;
    private CancellationTokenSource? _detectVideoCts;

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
                new FilePickerFileType("Все медиафайлы")
                    { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.mp4", "*.avi", "*.mkv", "*.mov"] }
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
            IsVideoSelected = false;
            await using var stream = await files[0].OpenReadAsync();
            ActiveImage = new Bitmap(stream);
            return;
        }

        IsVideoSelected = true;
        IsVideoLoaded = false;
        IsVideoEnded = false;
        IsPaused = false;

        if (_stockVideoCts != null)
            await _stockVideoCts.CancelAsync();
        _stockVideoCts = new CancellationTokenSource();

        if (_detectVideoCts != null)
            await _detectVideoCts.CancelAsync();

        _ = Task.Run(() => PlayOriginalVideo(ImagePath, _stockVideoCts.Token), _stockVideoCts.Token);
    }

    private void PlayOriginalVideo(string videoPath, CancellationToken token)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            IsVideoEnded = true;
            return;
        }

        using var frame = new Mat();

        while (true)
        {
            if (token.IsCancellationRequested || IsVideoLoaded)
                break;

            if (IsPaused)
            {
                if (token.WaitHandle.WaitOne(100))
                    break;
                continue;
            }

            if (!capture.Read(frame) || frame.Empty())
            {
                IsVideoEnded = true;
                break;
            }

            // Конвертируем BGR (OpenCV) в RGB (ImageSharp/Avalonia)
            using var rgbFrame = new Mat();
            Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.BGR2RGB);

            // Переводим в байты и создаем Bitmap для Avalonia
            byte[] imageBytes = rgbFrame.ToBytes(".jpg");
            using var ms = new MemoryStream(imageBytes);
            var bitmap = new Bitmap(ms);

            if (token.IsCancellationRequested || IsVideoLoaded)
                break;

            // Отправляем кадр в UI-поток
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { ActiveImage = bitmap; });

            // Задержка ~30 FPS, чтобы видео не летело
            if (token.WaitHandle.WaitOne(33))
                break;
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
        catch (HttpRequestException ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDetect))]
    private async Task Detect()
    {
        if (ModelPath is null || ImagePath is null)
            return;

        if (_detectVideoCts != null)
            await _detectVideoCts.CancelAsync();
        _detectVideoCts = new CancellationTokenSource();
        var token = _detectVideoCts.Token;

        CurrentFrame = null;
        IsVideoLoaded = false;

        var configuration = new YoloConfiguration
        {
            Confidence = (float)Confidence,
            KeepAspectRatio = true
        };

        YoloPredictorOptions options = new YoloPredictorOptions
        {
            Configuration = configuration
        };

        // Проверяем, что выбрано: картинка или видео
        string extension = Path.GetExtension(ImagePath).ToLower();
        var imageExtensions = new List<string> { ".jpg", ".jpeg", ".png" };

        if (imageExtensions.Contains(extension))
            await ProcessImage(ImagePath, ModelPath, options);
        else
        {
            IsVideoEnded = false;
            IsVideoLoaded = true;
            IsPaused = false;

            if (_stockVideoCts != null)
                await _stockVideoCts.CancelAsync();

            await ProcessVideo(ImagePath, ModelPath, options, token);

            if (!token.IsCancellationRequested)
                IsVideoEnded = true;
        }
    }

    [RelayCommand]
    private async Task Replay()
    {
        if (ImagePath is null)
            return;

        if (IsVideoLoaded)
        {
            IsVideoEnded = false;
            IsPaused = false;
            DetectCommand.Execute(null);
        }
        else
        {
            IsVideoEnded = false;
            IsPaused = false;

            if (_stockVideoCts != null)
                await _stockVideoCts.CancelAsync();
            _stockVideoCts = new CancellationTokenSource();
            _ = Task.Run(() => PlayOriginalVideo(ImagePath, _stockVideoCts.Token), _stockVideoCts.Token);
        }
    }

    [RelayCommand]
    private void Pause() => IsPaused = !IsPaused;

    private async Task ProcessImage(string imagePath, string modelPath, YoloPredictorOptions options)
    {
        using var predictor = new YoloPredictor(modelPath, options);
        using var image = await Image.LoadAsync<Rgb24>(imagePath);

        var result = await predictor.DetectAsync(image);
        using var plotted = await result.PlotImageAsync(image);

        using var ms = new MemoryStream();
        await plotted.SaveAsPngAsync(ms);
        ms.Position = 0;

        ActiveImage = new Bitmap(ms);
    }

    private async Task ProcessVideo(string imagePath, string modelPath, YoloPredictorOptions options,
        CancellationToken token)
    {
        using var predictor = new YoloPredictor(modelPath, options);
        using var capture = new VideoCapture(imagePath);
        using var matFrame = new Mat();

        if (!capture.IsOpened())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { Error = "Ошибка: Не удалось открыть видеофайл."; });
            return;
        }

        CurrentFrame = 0;

        while (true)
        {
            if (token.IsCancellationRequested)
                break;

            if (IsPaused)
            {
                try
                {
                    await Task.Delay(100, token);
                }
                catch
                {
                    break;
                }

                continue;
            }

            if (!capture.Read(matFrame) || matFrame.Empty())
                break;

            // 1. Кодируем кадр в ультрабыстрый разжатый BMP
            byte[] imageBytes = matFrame.ToBytes(".bmp");
            using var image = Image.Load<Rgb24>(imageBytes);

            // 2. Отправляем кадр в оригинальном соотношении сторон.
            var result = await predictor.DetectAsync(image);
            using var plotted = await result.PlotImageAsync(image);

            // 3. Переводим результат работы в Bitmap для Avalonia
            using var ms = new MemoryStream();
            await plotted.SaveAsJpegAsync(ms);
            ms.Position = 0;
            var bitmap = new Bitmap(ms);

            if (token.IsCancellationRequested)
                break;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested)
                    ActiveImage = bitmap;
            });

            CurrentFrame++;

            // 4. Ограничиваем FPS (~30 кадров/сек), чтобы не перегружать CPU
            try
            {
                await Task.Delay(33, token);
            }
            catch
            {
                break;
            }
        }
    }
}