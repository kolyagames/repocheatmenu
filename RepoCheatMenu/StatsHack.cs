using System.Collections.Generic;
using System.Reflection;

namespace RepoCheatMenu;

// Writes upgrade-level values directly into StatsManager's per-stat dictionaries.
// Local-only - never broadcast over the network - so the host can't overwrite it
// and it keeps working on any server regardless of whether the host has mods.
internal static class StatsHack
{
    private static readonly Dictionary<string, FieldInfo> _fields = new();

    private static Dictionary<string, int> GetDict(string statField)
    {
        if (!_fields.TryGetValue(statField, out FieldInfo fi))
        {
            fi = typeof(StatsManager).GetField(
                statField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _fields[statField] = fi;
        }

        return fi?.GetValue(StatsManager.instance) as Dictionary<string, int>;
    }

    public static void ForceStat(string statField, int value)
    {
        if (StatsManager.instance == null || PlayerAvatar.instance == null) return;

        Dictionary<string, int> dict = GetDict(statField);
        if (dict == null) return;

        string steamId = GameReflect.Get<string>(PlayerAvatar.instance, "steamID");
        if (!dict.TryGetValue(steamId, out int current) || current != value)
        {
            dict[steamId] = value;
        }
    }
}
