using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Boreas.Ui.Presentation;

/// <summary>
/// Minimal change notification.
/// </summary>
/// <remarks>
/// Hand-written to avoid an MVVM dependency for this small surface.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns and notifies only when the value actually changed.</summary>
    /// <remarks>
    /// <paramref name="storage"/> avoids <c>field</c>, a C# 14 contextual
    /// keyword in property accessors.
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
