using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Boreas.Ui.Presentation;

/// <summary>
/// Minimal change notification.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from an MVVM package. AGENTS.md requires a
/// license, maintenance and transitive-graph review before any dependency is
/// admitted, and thirty lines is not worth that review.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns and notifies only when the value actually changed.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }
}
