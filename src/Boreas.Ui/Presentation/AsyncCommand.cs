using System.Windows.Input;

namespace Boreas.Ui.Presentation;

/// <summary>
/// A command that knows it is running.
/// </summary>
/// <remarks>
/// <see cref="IsRunning"/> keeps progress in place and prevents a second press
/// from issuing a duplicate command.
/// </remarks>
public sealed class AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    : ObservableObject, ICommand
{
    private readonly Func<CancellationToken, Task> _execute = execute;

    /// <summary>Always executable unless the caller said otherwise.</summary>
    private readonly Func<bool> _canExecute = canExecute ?? (static () => true);

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

    /// <summary>
    /// The <see cref="ICommand"/> entry point, which XAML calls and which
    /// cannot be awaited.
    /// </summary>
    /// <remarks>
    /// XAML requires <c>async void</c>. Expected cancellation is swallowed;
    /// other exceptions propagate so broken channel contracts remain visible.
    /// </remarks>
    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

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
