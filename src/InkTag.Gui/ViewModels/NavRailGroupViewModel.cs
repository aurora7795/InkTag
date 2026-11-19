using CommunityToolkit.Mvvm.ComponentModel;

namespace InkTag.Gui.ViewModels;

/// <summary>
/// Represents an entry in the Library Navigation Rail (e.g. smart list or publisher/series group).
/// </summary>
public partial class NavRailGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public NavRailGroupViewModel() { }

    public NavRailGroupViewModel(string name, int count, string key)
    {
        Name = name;
        Count = count;
        Key = key;
    }
}
