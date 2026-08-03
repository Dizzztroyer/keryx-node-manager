using System.Windows;
using KeryxNodeManager.App.ViewModels;

namespace KeryxNodeManager.App.Views;

/// <summary>
/// Code-behind for the first-run wizard (brief §4). Deliberately thin - all step logic and
/// validation lives in WizardViewModel; this class only wires the two things a ViewModel can't do
/// itself (closing the Window, and a Skip button that isn't a plain "go to next step" action).
/// </summary>
public partial class WizardWindow : Window
{
    private readonly WizardViewModel _viewModel;

    /// <summary>True once the wizard was completed via Finish or Skip (both persist), false if the
    /// user closed the window some other way (Alt+F4/X) without going through either - App.xaml.cs
    /// uses this to decide whether to show the wizard again on the next launch.</summary>
    public bool Completed { get; private set; }

    public WizardWindow(WizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.WizardCompleted += OnWizardCompleted;
    }

    private void OnWizardCompleted()
    {
        Completed = true;
        Close();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        // No-op beyond the bound NextCommand - kept as a named handler only so a future step can
        // hook extra UI behaviour (e.g. scrolling back to top) without touching the ViewModel.
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        // FinishCommand does the actual save+close via WizardCompleted.
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        // Skip persists whatever has already been entered (never a silent discard) and marks the
        // wizard seen, same contract as Finish - see WizardViewModel.FinishAsync's doc comment.
        await _viewModel.FinishCommand.ExecuteAsync(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.WizardCompleted -= OnWizardCompleted;
        base.OnClosed(e);
    }
}
