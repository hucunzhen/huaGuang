using System.ComponentModel;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HuaGuang.Monitor.Views;

public partial class DashboardPage : MonitorPageBase
{
    readonly IScannerInputMethodGuard _inputMethodGuard;
    DashboardViewModel? _viewModel;
    IDisposable? _englishInputScope;

    public DashboardPage() : this(
        MauiProgram.Services.GetRequiredService<DashboardViewModel>(),
        MauiProgram.Services.GetRequiredService<IScannerInputMethodGuard>())
    {
    }

    public DashboardPage(DashboardViewModel viewModel, IScannerInputMethodGuard inputMethodGuard)
    {
        InitializeComponent();
        _inputMethodGuard = inputMethodGuard;
        BindingContext = viewModel;
        _viewModel = viewModel;
        viewModel.RequestScannerFocus = FocusScannerInputEntry;
        viewModel.RequestScannerInputMethodCycle = CycleScannerEnglishInput;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is DashboardViewModel viewModel)
        {
            _viewModel = viewModel;
            viewModel.RequestScannerFocus = FocusScannerInputEntry;
            viewModel.RequestScannerInputMethodCycle = CycleScannerEnglishInput;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.RefreshOnAppear();
            await viewModel.TryAutoStartAsync();
            if (viewModel.ShowScannerInput)
            {
                BeginScannerEnglishInput();
            }
        }
    }

    protected override void OnDisappearing()
    {
        EndScannerEnglishInput();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnDisappearing();
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardViewModel.ShowScannerInput))
        {
            return;
        }

        if (_viewModel?.ShowScannerInput == true)
        {
            BeginScannerEnglishInput();
            return;
        }

        EndScannerEnglishInput();
    }

    async void OnScannerInputCompleted(object? sender, EventArgs e)
    {
        if (_viewModel?.SubmitScannerInputCommand.CanExecute(null) == true)
        {
            await _viewModel.SubmitScannerInputCommand.ExecuteAsync(null);
        }

        EndScannerEnglishInput();
        if (_viewModel?.ShowScannerInput == true)
        {
            CycleScannerEnglishInput();
        }
    }

    void CycleScannerEnglishInput()
    {
        EndScannerEnglishInput();
        if (_viewModel?.ShowScannerInput == true)
        {
            BeginScannerEnglishInput();
        }
    }

    void OnScannerInputEntryFocused(object? sender, FocusEventArgs e) => BeginScannerEnglishInput();

    void FocusScannerInputEntry()
    {
        _ = FocusScannerInputEntryDelayedAsync();
    }

    async Task FocusScannerInputEntryDelayedAsync()
    {
        if (_viewModel?.ShowScannerInput != true)
        {
            return;
        }

        await Task.Delay(150);
        ScannerInputEntry?.Focus();
        BeginScannerEnglishInput();
    }

    void BeginScannerEnglishInput()
    {
        if (_viewModel?.ShowScannerInput != true || _englishInputScope is not null)
        {
            return;
        }

        _englishInputScope = _inputMethodGuard.EnterEnglishInputMode(ScannerInputEntry);
    }

    void EndScannerEnglishInput()
    {
        _englishInputScope?.Dispose();
        _englishInputScope = null;
    }

    void OnFullScreenClicked(object? sender, EventArgs e) => ToggleFullScreen();
}
