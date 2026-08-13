using System.Windows.Input;

namespace Boreas.Ui.Presentation;

/// <summary>
/// A command that knows it is running.
/// </summary>
/// <remarks>
/// <see cref="IsRunning"/> exists so a control can show progress in place,
/// keeping its own label and its own width, instead of being swapped for a
/// spinner. It also makes the command self-guarding: a second press while the
/// first is in flight does nothing, which is what stops a double-click from
/// becoming two start commands the service then has to serialise.
/// </remarks>
public sealed class AsyncCommand : ObservableObject, ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool> _canExecute;

    public AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute ?? (static () => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanExecute(object? parameter) => !IsRunning && _canExecute();

    public async void Execute(object? parameter) => await ExecuteAsync(CancellationToken.None);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!CanExecute(null))
        {
            return;
        }

        IsRunning = true;
        try
        {
            await _execute(cancellationToken);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
