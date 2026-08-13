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
    /// <remarks>
    /// The receiver is named <paramref name="storage"/> rather than
    /// <c>field</c>. Every call site passes <c>ref field</c> from inside a
    /// property accessor, where that identifier is the C# 14 contextual keyword
    /// for the synthesized backing field; naming the parameter the same thing
    /// made the declaration read as though it were that keyword, which it never
    /// is.
    /// </remarks>
    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        Raise(propertyName);
        return true;
    }
}
