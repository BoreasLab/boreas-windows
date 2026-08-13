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
    /// <c>async void</c> is forced by the interface, and an exception escaping
    /// one is unhandled: it does not reach a caller, it reaches the process.
    /// Cancellation is caught because it is expected, being the client's own
    /// request when a page unloads or the window closes, and reporting it would
    /// be reporting that something worked.
    ///
    /// Everything else is deliberately left to propagate. <see
    /// cref="Services.IControlChannel"/> makes failure a state rather than an
    /// exception, so an exception arriving here means an implementation broke
    /// that contract, and swallowing it would leave the interface showing a
    /// stale state with no indication that anything went wrong. That is the
    /// same judgement <see cref="Contracts.Unreachable"/> makes: a broken
    /// invariant is loud.
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
