using CommunityToolkit.Mvvm.ComponentModel;

namespace LfoDetector.ViewModels;

public partial class ConfigurationVM : ViewModelBase
{
    [ObservableProperty] private double _confidence;
    [ObservableProperty] private double _ioU;
    [ObservableProperty] private bool _applyAutoOrient;
    [ObservableProperty] private bool _suppressParallelInference;
}

