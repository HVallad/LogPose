# LogPose — OPTCGSim mod

BepInEx plugin for OPTCGSim (v1.42a, Unity 6, Mono) adding a full in-game replay viewer with
match history, combat-log fixes, complete replay (RZ1) output, and per-deck alternate card art
selection. Game install: `D:\OPSIM`.

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

**Match History** (`Replay/MatchHistoryUI.cs`) — a native-styled button on the main menu opens a
browser of every recorded game: your leader left, the opponent's right, colored WIN/LOSS
(detected from log lines, falling back to final life totals for lethal endings), date and game
number. Clicking a match auto-starts a board behind a loading cover and drops you straight into
that replay — this is the intended way in.

**In-game replay viewer** (`Replay/*.cs`) — plays any recorded `.rz1` back on the real game
board using the game's own card objects, so moves glide instead of teleporting and the whole
thing reads as a built-in feature.

- *Transport*: a native-styled panel (bottom right) with first/last, turn, action, and event
  stepping plus autoplay with speed control. Keyboard: `←/→` events, `↑/↓` actions,
  `PgUp/PgDn` turns, `Home/End`. "Actions" are the game's own log-line boundaries, so one step
  is one meaningful thing happening.
- *Synced combat log*: the side log panel replays in lockstep with the board, sticky-scrolled
  to the latest line, and adds synthetic narration for deck activity the vanilla log never
  describes (searches, mills, scries — with real card names, including the opponent's).
- *Search X-ray*: when a searcher resolves, every card that was looked at appears in a reveal
  row by the searching player's deck — the card being taken sits raised, then flies to hand
  while the rest bottom-deck. Duplicate copies each get their own slot, and clusters never mix
  the two players' deck activity.
- *Correctness*: reconstruction is validated against the CHK checksums embedded in the stream
  and the accuracy is shown in the panel (100% on well-formed LogPose recordings). Rest states
  (leader, characters, stage, cost-area DON, attached DON) render faithfully. One autosave can
  contain several games (rematches); the parser splits them via checksum signatures and lists
  each game separately. Recordings that lack the vanilla `.log` still get a synced combat log —
  line positions are reconstructed by anchoring narration against the move stream.
- *Getting in and out*: Match History is the front door; **F7** (file picker) and **F8** (open
  newest instantly) also work from a Solo v Self board. Exit with the panel's Exit button or
  just leave via Back to Main — the viewer tears itself down either way.

**Alt art selector** (`AltArt*.cs`) — press **F6** in the deck editor. Cards in the current deck
with variant art get `<` / `>` cycling; thumbnails refresh live; choices persist to
`Decks\<name>.deck.arts.json` when the deck is saved (the `.deck` file is untouched, so decks
stay vanilla-compatible). Variants apply in-game for the deck you play. Art files are looked up
as `Cards\<SET>\<ID>_p1.png` / `_alt1.png` (+ optional `_p1_small.jpg` thumbnail).

Note: art is client-side — opponents see their own local art, not your selection.

## Install

Grab `LogPose.dll` from the [latest release](https://github.com/HVallad/LogPose/releases) and
drop it into `<game>\BepInEx\plugins\`. Requires [BepInEx 5.4.23+ win-x64](https://github.com/BepInEx/BepInEx/releases)
extracted into the game folder first (`winhttp.dll` + `BepInEx\` next to `OPTCGSim.exe`).
Config appears at `BepInEx\config\com.hunter.logpose.cfg` after first run.

## Build from source

```
dotnet build LogPose/LogPose.csproj -c Release
copy LogPose\bin\Release\LogPose.dll D:\OPSIM\BepInEx\plugins\
```

The csproj references game assemblies from `D:\OPSIM\OPTCGSim_Data\Managed` and BepInEx from
`D:\OPSIM\BepInEx\core` — adjust paths if your install differs.

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
