using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CantYouSeeImBusy;

public class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("Can't You See I'm Busy###CYSIBConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(400, 340);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        var changed = false;

        var master = config.MasterEnabled;
        if (ImGui.Checkbox("Enabled", ref master))
        {
            config.MasterEnabled = master;
            changed = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Play an emote automatically when you open:");
        ImGui.Spacing();

        var map = config.MapEnabled;
        if (ImGui.Checkbox("Map (/navigate)", ref map))
        {
            config.MapEnabled = map;
            changed = true;
        }

        var crafting = config.CraftingLogEnabled;
        if (ImGui.Checkbox("Crafting Log (/study)", ref crafting))
        {
            config.CraftingLogEnabled = crafting;
            changed = true;
        }

        var gathering = config.GatheringLogEnabled;
        if (ImGui.Checkbox("Gathering Log (/study)", ref gathering))
        {
            config.GatheringLogEnabled = gathering;
            changed = true;
        }

        var fishing = config.FishingLogEnabled;
        if (ImGui.Checkbox("Fishing Log (/study)", ref fishing))
        {
            config.FishingLogEnabled = fishing;
            changed = true;
        }

        ImGui.Separator();

        var motionOnly = config.MotionOnly;
        if (ImGui.Checkbox("Motion only (skip the chat line)", ref motionOnly))
        {
            config.MotionOnly = motionOnly;
            changed = true;
        }

        ImGui.TextDisabled("Map re-opens are locked out for 13s to avoid self-interrupting.");

        ImGui.Separator();
        ImGui.TextWrapped(
            "All four windows' addon hooks are confirmed working. Enable " +
            "diagnostic logging below if something stops firing after a " +
            "game patch changes an addon name, and check /xllog for " +
            "'[CYSIB diag]' lines to find the new one.");

        var diag = config.DiagnosticLogging;
        if (ImGui.Checkbox("Diagnostic addon logging", ref diag))
        {
            config.DiagnosticLogging = diag;
            changed = true;
        }

        if (changed)
            config.Save();
    }
}
