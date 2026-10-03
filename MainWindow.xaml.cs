using ImageUpscaller.Models;
using ImageUpscaller.Services;
using ImageUpscaller.ViewModels;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageUpscaller;

public partial class MainWindow : Window
{
    private readonly ImagePipeline _pipeline = new();
    private readonly MainWindowViewModel _viewModel = new();
    private BitmapSource? _original;
    private BitmapSource? _originalPreview;
    private BitmapSource? _upscaledPreview;
    private string? _inputPath;
    private CancellationTokenSource? _activeCancellation;
    private TaskCompletionSource? _operationCompletion;
    private bool _showingUpscaled;
    private bool _closeRequested;
    private bool _allowClose;
    private bool _currentStageCanCancel;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += Window_Closing;
        SetModelAvailability();
    }

    private void SetModelAvailability()
    {
        if (File.Exists(_pipeline.ModelPath))
        {
            _viewModel.StatusMessage = "Choose an image to get started.";
        }
        else
        {
            _viewModel.StatusMessage = "Model not found. Add Models/realesrgan-x4plus.onnx to run offline.";
            _viewModel.EngineMessage = "Expected model: " + _pipeline.ModelPath;
        }
    }

    private async void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCompletion is not null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose an image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.avif|PNG images|*.png|JPEG images|*.jpg;*.jpeg|WebP images|*.webp|AVIF images|*.avif",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        BeginOperation(cancellable: true);
        _viewModel.StatusMessage = "Decoding image…";
        try
        {
            var decoded = await _pipeline.DecodeAsync(dialog.FileName, _activeCancellation!.Token);
            if (_closeRequested)
                return;

            _original = decoded.FullImage;
            _originalPreview = decoded.Preview;
            _upscaledPreview = null;
            _showingUpscaled = false;
            _inputPath = dialog.FileName;
            PreviewImage.Source = _originalPreview;
            EmptyPreview.Visibility = Visibility.Collapsed;
            PreviewLabel.Text = "ORIGINAL";
            CompareButton.Content = "Show result";
            CompareButton.IsEnabled = false;
            _viewModel.ImageSummary = $"{Path.GetFileName(dialog.FileName)}\n{_original.PixelWidth:N0} × {_original.PixelHeight:N0} px";
            _viewModel.StatusMessage = $"Ready to upscale · decoded in {decoded.DecodeTime.TotalSeconds:N1}s.";
            _viewModel.EngineMessage = "Local inference · GPU when available, CPU fallback";
        }
        catch (OperationCanceledException)
        {
            if (!_closeRequested)
                _viewModel.StatusMessage = "Image loading cancelled.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("image.decode_failed", ex, ("filename", Path.GetFileName(dialog.FileName)));
            if (!_closeRequested)
                MessageBox.Show(this, "This image could not be opened. Check that it is a valid PNG, JPEG, WebP, or AVIF file.\n\n" + ex.Message,
                    "Could not open image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CompleteOperation();
        }
    }

    private async void Upscale_Click(object sender, RoutedEventArgs e)
    {
        if (_original is null || _inputPath is null || _operationCompletion is not null)
            return;

        var outputScale = _viewModel.SelectedScale;
        var outputFormat = _viewModel.SelectedOutputFormat;
        int scaleFactor = (int)outputScale;
        string extension = outputFormat switch
        {
            OutputFormat.Webp => ".webp",
            OutputFormat.AvifQuality or OutputFormat.AvifTargetSize => ".avif",
            _ => ".png"
        };
        var dialog = new SaveFileDialog
        {
            Title = "Save upscaled image",
            Filter = outputFormat switch
            {
                OutputFormat.Webp => "WebP image|*.webp",
                OutputFormat.AvifQuality or OutputFormat.AvifTargetSize => "AVIF image|*.avif",
                _ => "PNG image|*.png"
            },
            DefaultExt = extension,
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_inputPath) + $"_{scaleFactor}x{extension}"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        BeginOperation(cancellable: true);
        _viewModel.StatusMessage = $"Preparing {scaleFactor}× upscale…";
        var progress = new Progress<UpscaleProgress>(p =>
        {
            if (_closeRequested)
                return;
            _viewModel.StatusMessage = p.Message;
        });

        var operationTimer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _pipeline.UpscaleAndSaveAsync(_original, _inputPath, dialog.FileName,
                outputScale, outputFormat, progress, _activeCancellation!.Token,
                format => Dispatcher.Invoke(() =>
                {
                    if (_closeRequested)
                        return;
                    _currentStageCanCancel = format is OutputFormat.AvifQuality or OutputFormat.AvifTargetSize;
                    _viewModel.StatusMessage = format switch
                    {
                        OutputFormat.Webp => "Upscale complete. Compressing high-quality WebP…",
                        OutputFormat.AvifQuality => "Upscale complete. Compressing high-quality AVIF…",
                        OutputFormat.AvifTargetSize => "Upscale complete. Compressing AVIF toward 10 MB…",
                        _ => "Upscale complete. Saving PNG…"
                    };
                    CancelButton.IsEnabled = _currentStageCanCancel;
                }));
            if (_closeRequested)
                return;

            _upscaledPreview = result.Preview;
            _showingUpscaled = true;
            PreviewImage.Source = result.Preview;
            PreviewLabel.Text = $"UPSCALED · {scaleFactor}×";
            CompareButton.Content = "Show original";
            CompareButton.IsEnabled = true;
            var sizeText = $"{result.OutputBytes / 1_000_000d:N1} MB";
            _viewModel.StatusMessage = $"Upscale complete · {result.Width:N0} × {result.Height:N0} px · {sizeText} · {result.TotalTime.TotalSeconds:N1}s.";
            _viewModel.EngineMessage = $"{_pipeline.Engine} · upscale {result.UpscaleTime.TotalSeconds:N1}s · encode {result.EncodeTime.TotalSeconds:N1}s";
            WorkProgress.Value = 100;
            System.Media.SystemSounds.Asterisk.Play();
            MessageBox.Show(this,
                $"Your upscaled image is ready.\n\n{result.Width:N0} × {result.Height:N0} px · {sizeText}\n\nSaved to:\n{dialog.FileName}",
                "Upscale complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            if (!_closeRequested)
            {
                WorkProgress.Value = 0;
                _viewModel.StatusMessage = "Upscaling cancelled.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("pipeline.failed", ex, ("filename", Path.GetFileName(_inputPath)),
                ("scale", scaleFactor), ("outputFormat", outputFormat), ("elapsedMs", operationTimer.ElapsedMilliseconds));
            if (!_closeRequested)
            {
                MessageBox.Show(this, "Upscaling failed. Your original image was not changed.\n\n" + ex.Message,
                    "Upscale error", MessageBoxButton.OK, MessageBoxImage.Error);
                _viewModel.StatusMessage = "Upscaling failed.";
            }
        }
        finally
        {
            CompleteOperation();
        }
    }

    private void BeginOperation(bool cancellable)
    {
        _activeCancellation = new CancellationTokenSource();
        _operationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _currentStageCanCancel = cancellable;
        SetBusy(true);
    }

    private void CompleteOperation()
    {
        var completed = _operationCompletion;
        _operationCompletion = null;
        _activeCancellation?.Dispose();
        _activeCancellation = null;
        _currentStageCanCancel = false;
        SetBusy(false);
        completed?.TrySetResult();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStageCanCancel)
        {
            _viewModel.StatusMessage = "Cancelling the current tile or AVIF encoder…";
            _activeCancellation?.Cancel();
            CancelButton.IsEnabled = false;
        }
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (_originalPreview is null || _upscaledPreview is null)
            return;

        _showingUpscaled = !_showingUpscaled;
        PreviewImage.Source = _showingUpscaled ? _upscaledPreview : _originalPreview;
        PreviewLabel.Text = _showingUpscaled ? "UPSCALED" : "ORIGINAL";
        CompareButton.Content = _showingUpscaled ? "Show original" : "Show result";
    }

    private void SetBusy(bool busy)
    {
        ChooseImageButton.IsEnabled = !busy;
        ScaleComboBox.IsEnabled = !busy;
        CompressionComboBox.IsEnabled = !busy;
        UpscaleButton.IsEnabled = !busy && _original is not null && File.Exists(_pipeline.ModelPath);
        CancelButton.IsEnabled = busy && _currentStageCanCancel;
        CompareButton.IsEnabled = !busy && _upscaledPreview is not null;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        if (_operationCompletion is { } operation)
        {
            e.Cancel = true;
            if (_closeRequested)
                return;

            _closeRequested = true;
            if (_currentStageCanCancel)
                _activeCancellation?.Cancel();
            _viewModel.StatusMessage = _currentStageCanCancel
                ? "Cancelling the active operation before closing…"
                : "Waiting for the current encode to finish before closing…";
            _ = CloseAfterOperationAsync(operation.Task);
            return;
        }

        _pipeline.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task CloseAfterOperationAsync(Task operation)
    {
        await operation.ConfigureAwait(false);
        await _pipeline.DisposeAsync().ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            _allowClose = true;
            Close();
        });
    }
}
