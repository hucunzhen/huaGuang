using System.Collections.ObjectModel;

namespace HuaGuang.Monitor.ViewModels;

internal static class GroupedCollectionHelper
{
    public static void ClearConfigGroups(ObservableCollection<TagConfigGroupViewModel> groups)
    {
        foreach (var group in groups.ToList())
        {
            group.Clear();
        }

        groups.Clear();
    }

    public static void ClearDashboardGroups(ObservableCollection<TagGroupViewModel> groups)
    {
        foreach (var group in groups.ToList())
        {
            group.Tags.Clear();
        }

        groups.Clear();
    }
}
