# Can't You See I'm Busy

A Dalamud plugin for FFXIV that plays a little animation for you when
you open the map or check a crafting/gathering/fishing log — because
apparently you're far too busy to just stand there.

## What it does

- Opening the world map or zone map plays **/navigate**, fresh every
  time — no cooldown, so it always restarts even if the previous one
  is still playing.
- Opening the Crafting Log, Gathering Log, or Fishing Log plays
  **/read**, which won't restart itself while its loop is still
  going — but either one can still interrupt the other at any time.
- Everything is toggleable individually, plus a master on/off switch.

## Installing

Can't You See I'm Busy isn't in the official Dalamud plugin list, so
it's installed via a custom repository:

1. In-game, open `/xlsettings` → **Experimental** → **Custom Plugin
   Repositories**.
2. Paste in:
   ```
   https://raw.githubusercontent.com/mr-reeh/Cant-You-See-Im-Busy/main/repo.json
   ```
3. Click the `+`, then **Save**.
4. Open `/xlplugins` and search for "Can't You See I'm Busy" to
   install it.

## Settings

Open the settings window in-game with `/cysib`, or via the gear icon
next to the plugin in the Plugin Installer. From there you can toggle:

- The plugin as a whole
- Each of the four windows individually (Map, Crafting Log, Gathering
  Log, Fishing Log)
- Motion-only mode, so the emote plays without a line in your chat log
- Diagnostic logging, for troubleshooting if a future game patch
  changes something (see below)

## Troubleshooting

If one of the four stops triggering (most likely after a game patch
changes an internal window name), or the interrupt/repeat behavior
seems off, turn on **Diagnostic addon logging** in the settings
window and check `/xllog` for lines starting with `[CYSIB diag]` —
they show the window's current internal name and whether the plugin
considers itself "self-busy" at the moment it tried to trigger.

---

## For developers

<details>
<summary>Building, project layout, and release process</summary>

### Building

1. Make sure `DALAMUD_HOME` is set (XIVLauncher users get this for
   free; XIVLauncher.Core / manual setups need to point it at their
   Dalamud dev folder, usually
   `%AppData%\XIVLauncher\addon\Hooks\dev`).
2. `dotnet restore`
3. `dotnet build -c Debug`

If `dotnet restore` errors on the SDK version in the `.csproj`'s first
line (`Dalamud.NET.Sdk/15.0.0`), check
https://www.nuget.org/packages/Dalamud.NET.Sdk for whatever's current
and bump it to match your installed Dalamud's API level.

### Loading it in-game for testing

1. In-game, run `/xlsettings` → Experimental → add the path to this
   project's output folder (`...\CantYouSeeImBusy\bin\x64\Debug`)
   under **Dev Plugin Locations**.
2. Open the plugin installer (`/xlplugins`) → Dev Tools tab → find
   "Can't You See I'm Busy" and enable it.
3. Rebuilding after a code change and reloading the plugin (or
   restarting the game) picks up the new DLL.

### What's hooked, under the hood

| Window | Addon | Emote |
|---|---|---|
| Map (world/zone) | `AreaMap` | `/navigate` |
| Crafting Log | `RecipeNote` | `/read` |
| Gathering Log | `GatheringNote` | `/read` |
| Fishing Log | `FishingNote` | `/read` |

All four use `AddonEvent.PostShow`, confirmed via diagnostic logging
to be what fires when the player actually opens the window (as
opposed to `PostSetup`/`PostOpen`, which only fire once per session
when the addon is first constructed).

### How the interrupt/repeat logic works

`/read` is an infinite loop, so `Character.Mode == EmoteLoop` (read
via FFXIVClientStructs) is exactly the signal meant to detect it, and
does so accurately — the plugin won't restart `/read` while its loop
is still going. `/navigate` deliberately has no such check: it's a
long one-shot animation (confirmed >8 seconds via live diagnostic
sampling) that `Character.Mode` never reflects at all, so there's no
reliable "still playing" flag to check anyway — rather than fake one
with a timer, `/navigate` just fires fresh on every map open.

Each trigger only checks its *own* state: `/navigate` never looks at
`/read`'s loop state, and `/read`'s check never looks at `/navigate`
either. That's what lets opening a log always fire `/read`
(interrupting an in-progress `/navigate`) and opening the map always
fire `/navigate` (interrupting an in-progress `/read`, and itself if
it's still going).

### Making a release build

`dotnet build -c Release` triggers DalamudPackager (bundled with
Dalamud.NET.Sdk) automatically. Look in
`CantYouSeeImBusy/bin/x64/Release/net10.0-windows/CantYouSeeImBusy/`
for a generated `CantYouSeeImBusy.json` manifest and a `latest.zip` —
that zip is the exact file to attach to the GitHub release, named
`latest.zip` so it matches the download URL already in `repo.json`.

### Notes

- Uses ECommons for `Chat.SendMessage` rather than a hand-rolled
  `ProcessChatBox` signature. See `Plugin.cs` for where to swap that
  out if you'd rather not take the dependency.
- Config persists via the standard `IPluginConfiguration` /
  `SavePluginConfig` flow.

</details>
