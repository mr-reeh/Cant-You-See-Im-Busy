using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace CantYouSeeImBusy;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Can't You See I'm Busy";

    private const string CommandName = "/cysib";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("CantYouSeeImBusy");
    private readonly ConfigWindow configWindow;

    // RecipeNote, GatheringNote, and FishingNote are all confirmed working
    // live (GatheringNote and RecipeNote confirmed by testing; FishingNote
    // confirmed via diagnostic logging after FishGuide, the original
    // guess, turned out wrong).
    private readonly Dictionary<string, (Func<Configuration, bool> Enabled, string Emote)> trackedAddons = new()
    {
        ["AreaMap"] = (c => c.MapEnabled, "/navigate"),
        ["RecipeNote"] = (c => c.CraftingLogEnabled, "/read"),
        ["GatheringNote"] = (c => c.GatheringLogEnabled, "/read"),
        ["FishingNote"] = (c => c.FishingLogEnabled, "/read"),
    };

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // ECommons gives us Chat.SendMessage, a maintained, version-tolerant
        // way to inject text into chat, instead of hand-rolling a
        // ProcessChatBox signature scan ourselves.
        ECommonsMain.Init(pluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigWindow;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Can't You See I'm Busy settings."
        });

        foreach (var addonName in trackedAddons.Keys)
            AddonLifecycle.RegisterListener(AddonEvent.PostShow, addonName, OnTrackedAddonShow);

        // Diagnostic net: logs any Open/Show/Setup/Refresh for addons whose
        // name looks map/crafting/gathering/fishing-related, gated by the
        // DiagnosticLogging config toggle rather than always-on spam.
        AddonLifecycle.RegisterListener(AddonEvent.PostOpen, OnAnyAddonDiagnostic);
        AddonLifecycle.RegisterListener(AddonEvent.PostShow, OnAnyAddonDiagnostic);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnAnyAddonDiagnostic);
        AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, OnAnyAddonDiagnostic);

        Log.Information("Can't You See I'm Busy loaded.");
    }

    private void OnAnyAddonDiagnostic(AddonEvent type, AddonArgs args)
    {
        if (!Configuration.DiagnosticLogging)
            return;

        var name = args.AddonName;
        if (name.Contains("Map", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Recipe", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Craft", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Gather", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Fish", StringComparison.OrdinalIgnoreCase))
        {
            Log.Information($"[CYSIB diag] {type} fired for addon '{name}'.");
        }
    }

    // /read is an infinite loop, so Character.Mode == EmoteLoop is exactly
    // the signal meant to detect it (confirmed False for /navigate, which
    // is why /navigate doesn't use this check at all — see below).
    private static unsafe bool IsCharacterInEmoteLoop()
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return false;

        var character = (Character*)localPlayer.Address;
        if (character == null)
            return false;

        return character->Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop;
    }

    private void OnTrackedAddonShow(AddonEvent type, AddonArgs args)
    {
        if (!trackedAddons.TryGetValue(args.AddonName, out var trigger))
            return;

        var isNavigate = trigger.Emote == "/navigate";
        // /navigate always fires fresh on every map open — no self-lock.
        // /read is the one that needs a check, since it loops forever
        // until something interrupts it.
        var selfBusy = !isNavigate && IsCharacterInEmoteLoop();

        // Log every guard's state up front, before any early return, so a
        // silent bail-out (combat, cutscene, master toggle, etc.) is
        // visible in /xllog instead of just looking like nothing happened.
        if (Configuration.DiagnosticLogging)
        {
            Log.Information($"[CYSIB diag] {args.AddonName} PostShow: "
                + $"masterEnabled={Configuration.MasterEnabled}, "
                + $"windowEnabled={trigger.Enabled(Configuration)}, "
                + $"loggedIn={ClientState.IsLoggedIn}, "
                + $"inCutscene={Condition[ConditionFlag.OccupiedInCutSceneEvent]}, "
                + $"inCombat={Condition[ConditionFlag.InCombat]}, "
                + $"casting={Condition[ConditionFlag.Casting]}, "
                + $"selfBusy={selfBusy}");
        }

        if (!Configuration.MasterEnabled)
            return;

        if (!trigger.Enabled(Configuration))
            return;

        if (!ClientState.IsLoggedIn)
            return;

        // Don't fire during cutscenes, combat, or while already casting —
        // these emotes would just get rejected server-side anyway.
        if (Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Condition[ConditionFlag.InCombat]
            || Condition[ConditionFlag.Casting])
            return;

        // Only refuse to fire if this SAME emote is the reason we'd be
        // repeating ourselves. /navigate has no such check at all — it
        // always fires fresh. /read's check never looks at /navigate's
        // state either, so opening the map always fires /navigate
        // (interrupting an in-progress /read) and opening a log always
        // fires /read (interrupting an in-progress /navigate).
        if (selfBusy)
            return;

        var command = Configuration.MotionOnly ? $"{trigger.Emote} motion" : trigger.Emote;
        Chat.SendMessage(command);
    }

    private void DrawUi() => windowSystem.Draw();

    private void ToggleConfigWindow() => configWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        configWindow.Toggle();
    }

    public void Dispose()
    {
        foreach (var addonName in trackedAddons.Keys)
            AddonLifecycle.UnregisterListener(AddonEvent.PostShow, addonName, OnTrackedAddonShow);

        AddonLifecycle.UnregisterListener(OnAnyAddonDiagnostic);

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigWindow;
        windowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }
}
