using Dalamud.Configuration;
using Dalamud.Plugin;

namespace CantYouSeeImBusy;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // Master kill switch, independent of the per-window toggles below.
    public bool MasterEnabled { get; set; } = true;

    public bool MapEnabled { get; set; } = true;
    public bool CraftingLogEnabled { get; set; } = true;
    public bool GatheringLogEnabled { get; set; } = true;
    public bool FishingLogEnabled { get; set; } = true;

    // Note: the map's 13-second re-trigger lock is hardcoded in Plugin.cs
    // (MapLockSeconds), not stored here — it's meant to stay fixed rather
    // than be user-tunable.

    // Appends " motion" to whichever emote is sent, so it plays the
    // animation only, without echoing a line into chat.
    public bool MotionOnly { get; set; } = true;

    // Logs [CYSIB diag] lines in /xllog for any addon whose name looks
    // related to map/crafting/gathering/fishing, on Open/Show/Setup/Refresh.
    // Useful for confirming or correcting the addon names this plugin hooks.
    public bool DiagnosticLogging { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
