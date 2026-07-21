using System.Reflection;

namespace Karate.Services;

public record DriverUpdate(string Title, string Model, string Manufacturer, string HardwareId);

/// <summary>
/// Searches Windows Update for available driver updates via the Windows Update
/// Agent COM API (Microsoft.Update.Session). Search only — installation stays
/// in the user's hands via Windows Update itself.
/// </summary>
public static class DriverUpdateService
{
    public static Task<List<DriverUpdate>?> SearchAsync() => Task.Run(Search);

    private static List<DriverUpdate>? Search()
    {
        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null)
                return null;
            var session = Activator.CreateInstance(sessionType);
            if (session is null)
                return null;

            var searcher = Invoke(session, "CreateUpdateSearcher");
            if (searcher is null)
                return null;
            SetProp(searcher, "Online", true);

            var result = Invoke(searcher, "Search", "IsInstalled=0 and Type='Driver'");
            var updates = GetProp(result, "Updates");
            if (updates is null)
                return null;

            var count = (int)(GetProp(updates, "Count") ?? 0);
            var list = new List<DriverUpdate>();
            for (int i = 0; i < count; i++)
            {
                var update = GetProp(updates, "Item", i);
                if (update is null)
                    continue;
                list.Add(new DriverUpdate(
                    GetStr(update, "Title"),
                    GetStr(update, "DriverModel"),
                    GetStr(update, "DriverManufacturer"),
                    GetStr(update, "DriverHardwareID")));
            }
            return list;
        }
        catch
        {
            // COM failure, no network, WU service disabled, …
            return null;
        }
    }

    // Late-bound IDispatch helpers — the WUA interop assembly is not available
    // for .NET Core, so we go through reflection.
    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? GetProp(object? target, string name, params object[] args) =>
        target?.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, args);

    private static void SetProp(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static string GetStr(object target, string name)
    {
        try { return GetProp(target, name) as string ?? ""; }
        catch { return ""; }
    }
}
