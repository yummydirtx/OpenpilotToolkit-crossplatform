using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenpilotToolkit.Avalonia.ViewModels;

namespace OpenpilotToolkit.Avalonia.Views;

public partial class PassphrasePromptWindow : Window
{
    public PassphrasePromptWindow()
        : this(string.Empty)
    {
    }

    public PassphrasePromptWindow(string promptMessage)
    {
        InitializeComponent();
        DataContext = new PassphrasePromptViewModel
        {
            PromptMessage = promptMessage
        };
        Opened += (_, _) => PassphraseTextBox.Focus();
    }

    private void RetryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(PassphraseTextBox.Text);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
