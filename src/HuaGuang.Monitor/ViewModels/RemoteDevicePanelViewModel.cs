using System.Collections.ObjectModel;

namespace HuaGuang.Monitor.ViewModels;

public sealed class RemoteDevicePanelViewModel
{
    public required string DeviceKey { get; init; }
    public string DeviceId { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public ObservableCollection<TagGroupViewModel> TagGroups { get; } = [];
}
