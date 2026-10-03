using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageUpscaller.Models;

namespace ImageUpscaller.ViewModels;

/// <summary>Holds the view state and strongly typed selections; file dialogs and image presentation remain in the view.</summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private UpscaleScale _selectedScale = UpscaleScale.X4;
    private OutputFormat _selectedOutputFormat = OutputFormat.Png;
    private string _statusMessage = "Choose an image to get started.";
    private string _imageSummary = "No image selected";
    private string _engineMessage = "Local inference · GPU when available, CPU fallback";

    public event PropertyChangedEventHandler? PropertyChanged;

    public UpscaleScale SelectedScale
    {
        get => _selectedScale;
        set => SetField(ref _selectedScale, value);
    }

    public OutputFormat SelectedOutputFormat
    {
        get => _selectedOutputFormat;
        set => SetField(ref _selectedOutputFormat, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string ImageSummary
    {
        get => _imageSummary;
        set => SetField(ref _imageSummary, value);
    }

    public string EngineMessage
    {
        get => _engineMessage;
        set => SetField(ref _engineMessage, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
