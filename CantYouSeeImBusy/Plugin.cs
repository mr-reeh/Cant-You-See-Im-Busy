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
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
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

    // Which of our own emotes (if any) is the reason the character is
    // currently in a "busy" Mode. Used only to tell "still playing the
    // same thing we just triggered" apart from "playing something else" —
    // the latter should still fire and interrupt. Cleared whenever the
    // character isn't in a busy Mode, so stale values can't linger.
    private string? currentlyPlayingEmote;

    // --- TEMPORARY DIAGNOSTICS ---
    // Character.Mode (EmoteLoop/AnimLock/InPositionLoop) turned out NOT to
    // reflect non-looping "prop" emotes like /navigate or /read (confirmed
    // by live testing — busy stayed False the whole time). Rather than
    // guess at another unverified internal field, this samples Mode and
    // ModeParam repeatedly for a few seconds after we trigger one of our
    // emotes, so real data from an actual playback tells us what (if
    // anything) changes, instead of another blind guess.
    private DateTime watchUntil = DateTime.MinValue;
    private DateTime lastWatchLog = DateTime.MinValue;
    // --- END TEMPORARY DIAGNOSTICS ---

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        PluginInstance = this;

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

        // --- TEMPORARY DIAGNOSTICS ---
        Framework.Update += OnFrameworkUpdateDiagnostic;
        // --- END TEMPORARY DIAGNOSTICS ---

        // Diagnostic net: logs any Open/Show/Setup/Refresh for addons whose
        // name looks map/crafting/gathering/fishing-related, gated by the
        // DiagnosticLogging config toggle rather than always-on spam.
        AddonLifecycle.RegisterListener(AddonEvent.PostOpen, OnAnyAddonDiagnostic);
        AddonLifecycle.RegisterListener(AddonEvent.PostShow, OnAnyAddonDiagnostic);
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnAnyAddonDiagnostic);
        AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, OnAnyAddonDiagnostic);

        Log.Information("Can't You See I'm Busy loaded.");
    }

    // --- TEMPORARY DIAGNOSTICS ---
    // Samples Character.Mode/ModeParam roughly every 200ms for 8 seconds
    // after a tracked emote fires, so we can see in /xllog exactly how (or
    // whether) they change across the real animation, rather than guess.
    private static unsafe void OnFrameworkUpdateDiagnostic(IFramework framework)
    {
        if (DateTime.Now > PluginInstance!.watchUntil)
            return;

        if (DateTime.Now - PluginInstance.lastWatchLog < TimeSpan.FromMilliseconds(200))
            return;
        PluginInstance.lastWatchLog = DateTime.Now;

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        var character = (Character*)localPlayer.Address;
        if (character == null)
            return;

        Log.Information($"[CYSIB diag] t={DateTime.Now:HH:mm:ss.fff} Mode={character->Mode} ModeParam={character->ModeParam} ActorControlFlags={character->ActorControlFlags}");
    }

    private static Plugin? PluginInstance;
    // --- END TEMPORARY DIAGNOSTICS ---

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

    // Reads the local player's CharacterModes directly via FFXIVClientStructs.
    // ConditionFlag.Emoting does NOT reflect /navigate or /read (confirmed
    // False while /navigate was visibly still playing) — Character.Mode is
    // the field the game itself actually uses. EmoteLoop/AnimLock/
    // InPositionLoop are the three Mode values tied to playing some kind of
    // emote/animation; Normal (and everything else) means free to act.
    private static unsafe bool IsCharacterBusyWithEmote()
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return false;

        var character = (Character*)localPlayer.Address;
        if (character == null)
            return false;

        var mode = character->Mode;
        return mode is CharacterModes.EmoteLoop or CharacterModes.AnimLock or CharacterModes.InPositionLoop;
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

        var busy = IsCharacterBusyWithEmote();

        if (Configuration.DiagnosticLogging)
            Log.Information($"[CYSIB diag] {args.AddonName} PostShow: busy={busy}, currentlyPlaying={currentlyPlayingEmote ?? "none"}");

        if (!busy)
            currentlyPlayingEmote = null;

        // Only refuse to fire if the SAME emote is still the reason we're
        // busy — this is what stops /navigate (or /read) from restarting
        // itself on a quick reopen, while still letting /navigate and
        // /read interrupt each other freely.
        if (busy && currentlyPlayingEmote == trigger.Emote)
            return;

        var command = Configuration.MotionOnly ? $"{trigger.Emote} motion" : trigger.Emote;
        Chat.SendMessage(command);
        currentlyPlayingEmote = trigger.Emote;

        // --- TEMPORARY DIAGNOSTICS ---
        if (Configuration.DiagnosticLogging)
        {
            watchUntil = DateTime.Now.AddSeconds(8);
            Log.Information($"[CYSIB diag] sent '{command}', watching Mode for 8s...");
        }
        // --- END TEMPORARY DIAGNOSTICS ---
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

        // --- TEMPORARY DIAGNOSTICS ---
        Framework.Update -= OnFrameworkUpdateDiagnostic;
        // --- END TEMPORARY DIAGNOSTICS ---

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigWindow;
        windowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }
}
