# Can't You See I'm Busy

Plays an emote automatically when you open certain game windows:

| Window | Addon hooked | Emote | Config toggle | Self-lock |
|---|---|---|---|---|
| Map (world/zone) | `AreaMap` | `/navigate` | Map | 13s (hardcoded) |
| Crafting Log | `RecipeNote` | `/study` | Crafting Log | None |
| Gathering Log | `GatheringNote` | `/study` | Gathering Log | None |
| Fishing Log | `FishingNote` | `/study` | Fishing Log | None |

All four addon names are confirmed working live. All hooks use
`AddonEvent.PostShow` — confirmed (via diagnostic logging) to be what
fires when the player actually opens the window, as opposed to
`PostSetup`/`PostOpen`, which only fire once per session when the addon
is first constructed.

## Installing (for users)

1. In-game, run `/xlsettings` → Experimental → Custom Plugin Repositories.
2. Paste in `https://raw.githubusercontent.com/mr-reeh/Cant-You-See-Im-Busy/main/repo.json`, click the `+`, save.
3. Open `/xlplugins`, search for "Can't You See I'm Busy", install.

## Interrupt behavior

Only the map self-locks against rapid re-triggering, for a fixed 13
seconds (`MapLockSeconds` in `Plugin.cs` — hardcoded intentionally, not
user-configurable). This exists because `ConditionFlag.Emoting` does
NOT reflect `/navigate` or `/study` (confirmed False while `/navigate`
was visibly still playing), so there's no reliable game-exposed flag to
check — the lock is our own approximation of the animation's length.

The three log windows have no lock at all, so opening one always fires
`/study` immediately — including interrupting an in-progress
`/navigate`. And since the map's lock only guards against itself,
opening the map right after a log's `/study` still fires `/navigate`
and interrupts that too. In short: `/navigate` and `/study` can always
interrupt each other; only the map can't interrupt itself within 13s.

## If an addon hook stops firing (e.g. after a game patch)

Open the in-game config window (`/cysib`, or the gear icon in the
Plugin Installer) and check **Diagnostic addon logging**. Then open the
window in question and check `/xllog` for lines like:

```
[CYSIB diag] PostShow fired for addon 'SomeActualName'.
```

Update the `trackedAddons` dictionary at the top of `Plugin.cs` with
whatever name shows up there and rebuild.

## Making a release build

`dotnet build -c Release` triggers DalamudPackager (bundled with
Dalamud.NET.Sdk) automatically. Look in
`CantYouSeeImBusy/bin/x64/Release/net10.0-windows/CantYouSeeImBusy/` for
a generated `CantYouSeeImBusy.json` manifest and a `latest.zip` — that
zip is the exact file to attach to the GitHub release.

## Building

1. Make sure `DALAMUD_HOME` is set (XIVLauncher users get this for free;
   XIVLauncher.Core / manual setups need to point it at their Dalamud dev
   folder, usually `%AppData%\XIVLauncher\addon\Hooks\dev`).
2. `dotnet restore`
3. `dotnet build -c Debug`

If `dotnet restore` errors on the SDK version in the `.csproj`'s first
line (`Dalamud.NET.Sdk/15.0.0`), check
https://www.nuget.org/packages/Dalamud.NET.Sdk for whatever's current
and bump it — the SDK version needs to track your installed Dalamud's
API level.

## Loading it in-game for testing

1. In-game, run `/xlsettings` → Experimental → add the path to this
   project's output folder (`...\CantYouSeeImBusy\bin\x64\Debug`) under
   **Dev Plugin Locations**.
2. Open the plugin installer (`/xlplugins`) → Dev Tools tab → find
   "Can't You See I'm Busy" and enable it.
3. Rebuilding after a code change and reloading the plugin (or
   restarting the game) picks up the new DLL.

## Commands

- `/cysib` — open the settings window. Also reachable from the gear
  icon in the Plugin Installer.

## Settings window

Toggles for: master enable, each of the four tracked windows
individually, motion-only (skip the chat line), and diagnostic addon
logging (see above). No lock-duration control by design — see
"Interrupt behavior" above.

## Notes

- Uses ECommons for `Chat.SendMessage` rather than a hand-rolled
  `ProcessChatBox` signature. See `Plugin.cs` for where to swap that out
  if you'd rather not take the dependency.
- Config persists via the standard `IPluginConfiguration` /
  `SavePluginConfig` flow.
