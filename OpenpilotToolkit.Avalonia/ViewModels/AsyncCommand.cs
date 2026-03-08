using System.Windows.Input;

namespace OpenpilotToolkit.Avalonia.ViewModels;

public sealed class AsyncCommand(Func<Task> executeAsync, Func<bool>? canExecute = null) : ICommand
{
    private readonly Func<Task> _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
    private readonly Func<bool>? _canExecute = canExecute;
    private bool _isRunning;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isRunning && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync().ConfigureAwait(true);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
