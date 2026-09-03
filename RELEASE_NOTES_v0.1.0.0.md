## v0.1.0.0

Initial release.

- Plays `/navigate` automatically when you open the world or zone map.
- Plays `/study` automatically when you open the Crafting Log, Gathering
  Log, or Fishing Log.
- `/navigate` and `/study` can interrupt each other freely.
- The map won't re-trigger itself for 13 seconds after opening, so
  quickly closing and reopening it doesn't restart the animation.
- Settings window (`/cysib`, or the gear icon in the Plugin Installer):
  master enable, per-window toggles, motion-only mode (skip the chat
  line), and diagnostic addon logging for troubleshooting after future
  game patches.

Requires Dalamud API level 15.
