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
        if (ImGui.Checkbox("Crafting Log (/read)", ref crafting))
        {
            config.CraftingLogEnabled = crafting;
            changed = true;
        }

        var gathering = config.GatheringLogEnabled;
        if (ImGui.Checkbox("Gathering Log (/read)", ref gathering))
        {
            config.GatheringLogEnabled = gathering;
            changed = true;
        }

        var fishing = config.FishingLogEnabled;
        if (ImGui.Checkbox("Fishing Log (/read)", ref fishing))
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

        ImGui.TextDisabled(
            "Neither emote restarts itself while it's still playing, but " +
            "/navigate and /read can always interrupt each other.");

        ImGui.Separator();
        ImGui.TextWrapped(
            "All four windows' addon hooks are confirmed working. Diagnostic " +
            "logging also prints the character's busy state on every trigger " +
            "(useful if the interrupt behavior above ever misbehaves), and " +
            "logs any addon name that looks map/crafting/gathering/fishing- " +
            "related if a future game patch breaks one of the hooks — check " +
            "/xllog for '[CYSIB diag]' lines.");

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
