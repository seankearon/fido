using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Fido.Mvvm;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base. Hand-rolled to keep the
/// dependency surface to just Avalonia (no CommunityToolkit.Mvvm).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Assigns <paramref name="value"/> to <paramref name="field"/> and raises
    /// <see cref="PropertyChanged"/> when it actually changes. Returns true if it changed.</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
