using Fido.Models;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>
/// One radio row in the gear popover's default-tool list: a configured tool, or the trailing
/// "No default (equal weight)" row (<see cref="Index"/> = <see cref="AppConfig.NoDefaultEditor"/>).
/// The window persists the choice when <see cref="IsSelected"/> flips true.
/// </summary>
public sealed class DefaultToolChoice : ObservableObject
{
    public DefaultToolChoice(int index, string name)
    {
        Index = index;
        Name = name;
    }

    /// <summary>Position in <see cref="AppConfig.Editors"/>, or <see cref="AppConfig.NoDefaultEditor"/>.</summary>
    public int Index { get; }

    public string Name { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
