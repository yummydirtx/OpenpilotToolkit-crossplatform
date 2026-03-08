namespace OpenpilotToolkit.Avalonia.ViewModels;

public sealed class PassphrasePromptViewModel : ViewModelBase
{
    private string _passphrase = string.Empty;
    private string _promptMessage = string.Empty;

    public string PromptMessage
    {
        get => _promptMessage;
        set => SetProperty(ref _promptMessage, value);
    }

    public string Passphrase
    {
        get => _passphrase;
        set => SetProperty(ref _passphrase, value ?? string.Empty);
    }
}
