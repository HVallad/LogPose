# LogPose — OPTCGSim mod

BepInEx plugin for OPTCGSim (v1.42a, Unity 6, Mono) adding combat-log fixes, complete replay
(RZ1) output, and per-deck alternate card art selection. Game install: `D:\OPSIM`.

## Layout

| Path | What |
|------|------|
| `LogPose/` | Plugin source (C#, netstandard2.1, Harmony patches) |
| `analysis/log-coverage-and-don-analysis.md` | Reverse-engineered RZ1 replay format + all logging gaps (DON!! focus) |
| `tools/Fetch-AltArts.ps1` | Downloads official parallel-art images + makes `_small` thumbnails |
| `decompiled/` | ilspycmd output of the game's `Assembly-CSharp.dll` (local only — gitignored, not redistributable) |

## Features

**Replay stream completeness** (`ReplaySyncPatches.cs`) — the base game never emits RZ1 lines for
the refresh-phase untap (leader/characters/stage/**cost-area DON**), attacker rest, blocker rest,
or manual/effect tap-untap. The plugin re-publishes affected cards through the game's own
`ReplaySync_EmitCurrentZoneState`, so replayers (OPTCGReplay) finally see correct DON!! and rest
states. Config: `EmitMissingReplayLines`.

**Clean combat logs** (`CombatLogPatches.cs`) — alongside each autosaved log the plugin writes
`<stamp>.clean.log` (markup + zero-width chars stripped, UTF-8, human lines only) and
`<stamp>.rz1` (replay lines only). Config: `WriteCleanLog`, `WriteReplayFile`, `LogFontSize`.

**Alt art selector** (`AltArt*.cs`) — press **F6** in the deck editor. Cards in the current deck
with variant art get `<` / `>` cycling; thumbnails refresh live; choices persist to
`Decks\<name>.deck.arts.json` when the deck is saved (the `.deck` file is untouched, so decks
stay vanilla-compatible). Variants apply in-game for the deck you play. Art files are looked up
as `Cards\<SET>\<ID>_p1.png` / `_alt1.png` (+ optional `_p1_small.jpg` thumbnail).

Note: art is client-side — opponents see their own local art, not your selection.

## Build & install

```
dotnet build LogPose/LogPose.csproj -c Release
copy LogPose\bin\Release\LogPose.dll D:\OPSIM\BepInEx\plugins\
```

BepInEx 5.4.23.5 (win x64) is installed in `D:\OPSIM` (winhttp.dll + BepInEx folder). Config
appears at `D:\OPSIM\BepInEx\config\com.hunter.logpose.cfg` after first run.

## Fetching more alt arts

OP01 is already populated (110 parallels). For more sets:

```
powershell -ExecutionPolicy Bypass -File tools\Fetch-AltArts.ps1 -Sets OP02,OP03
powershell -ExecutionPolicy Bypass -File tools\Fetch-AltArts.ps1 -All
```

Images come from the official EN card database (`en.onepiece-cardgame.com`), personal use.
Re-running skips existing files. If a game update replaces `Assembly-CSharp.dll`, rebuild the
plugin (and re-run ilspycmd if you want fresh decompiled sources); if a patch renames one of the
patched methods the corresponding Harmony patch will log an error on startup and only that
feature degrades.
