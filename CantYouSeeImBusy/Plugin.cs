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

namespace CantYouSeeImBusy;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Can't You See I'm Busy";

    private const string CommandName = "/cysib";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("CantYouSeeImBusy");
    private readonly ConfigWindow configWindow;

    // Only the map self-locks against rapid re-triggering (hardcoded,
    // permanent — see MapLockSeconds below). /study on the three logs has
    // no lock at all, so opening a log always fires /study immediately,
    // interrupting an in-progress /navigate — and since /navigate's lock
    // is scoped to itself only, opening the map after a log's /study still
    // fires and interrupts it too. LockSeconds is null for triggers that
    // should never self-lock.
    //
    // RecipeNote, GatheringNote, and FishingNote are all confirmed
    // working live (GatheringNote and RecipeNote confirmed by testing;
    // FishingNote confirmed via diagnostic logging after FishGuide,
    // the original guess, turned out wrong).
    private const double MapLockSeconds = 13.0;

    private readonly Dictionary<string, (Func<Configuration, bool> Enabled, string Emote, double? LockSeconds)> trackedAddons = new()
    {
        ["AreaMap"] = (c => c.MapEnabled, "/navigate", MapLockSeconds),
        ["RecipeNote"] = (c => c.CraftingLogEnabled, "/study", null),
        ["GatheringNote"] = (c => c.GatheringLogEnabled, "/study", null),
        ["FishingNote"] = (c => c.FishingLogEnabled, "/study", null),
    };

    // Only populated for triggers that have a LockSeconds value (currently
    // just AreaMap). ConditionFlag.Emoting does NOT reflect these prop
    // emotes (confirmed False while /navigate was visibly still playing),
    // so this is our own approximation of "still busy" instead of trusting
    // a game-exposed flag that doesn't apply here.
    private readonly Dictionary<string, DateTime> lockUntil = new();

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

    private void OnTrackedAddonShow(AddonEvent type, AddonArgs args)
    {
        if (!Configuration.MasterEnabled)
            return;

        if (!trackedAddons.TryGetValue(args.AddonName, out var trigger))
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

        // Only fires for triggers with a LockSeconds value (the map).
        // /study on the logs has none, so it always fires — which is what
        // lets it interrupt an in-progress /navigate.
        if (trigger.LockSeconds is { } lockSeconds)
        {
            if (lockUntil.TryGetValue(args.AddonName, out var until) && DateTime.Now < until)
                return;

            lockUntil[args.AddonName] = DateTime.Now.AddSeconds(lockSeconds);
        }

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
