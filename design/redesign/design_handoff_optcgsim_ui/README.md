# Handoff: OPTCGSim UI reskin (LogPose mod)

## Overview
A full visual redesign of OPTCGSim — the fan-made Unity simulator for the One Piece Card Game —
delivered as static 1920×1080 HTML mockups plus a design system and a 9-slice sprite manifest.
The reskin is applied at runtime by the LogPose BepInEx/Harmony mod: sprites, fonts, colors,
sizes and anchors are replaced; new panels are built in code. Card images and playmat art are
licensed game content and are NOT redesigned — the layout sits around them.

Approved direction: **main menu "Chart"** (option `1c` — three destination cards over a lit
field with a persistent top bar). All other screens follow the same language.

## About the design files
The HTML files in this bundle are **design references**, not production code. They are
prototypes showing intended look, sizing and behaviour. The task is to recreate them in the
game's existing environment — **Unity uGUI, patched at runtime through Harmony** — using the
mod's established patterns: swap `Image.sprite` on existing elements, replace TMP font assets,
set `RectTransform` anchors/sizes, and instantiate new panels from code. Do not attempt to
embed HTML or a web view.

## Fidelity
**High-fidelity.** Colors, type sizes, spacing, radii and component states are final and are
specified in px at the 1920×1080 reference resolution. Reproduce them exactly; everything
scales linearly to 4K (×2) because all sprites are 9-sliced and all text is real TMP text.

## Colorways
Two complete sets are included — implement one, keep the other as a themable token swap:

- `OPTCGSim Redesign.dc.html` — **Nocturne** (blurple accent `#9184d9`).
- `OPTCGSim Redesign - Batsu.dc.html` — **Batsu brand** (magenta accent `#d81fb4`, brand cyan
  field), matching Batsu's new logo. Only fills change; every size and slice margin is identical.

The swap table at the end of `sprite-manifest.md` maps one to the other. Build the sprite set
once per colorway and expose a setting to switch.

## Screens / views

Each screen below is an option id in the HTML files (visible badge, e.g. `2a`). Open the file
and zoom to that frame for the pixel reference.

### 1c — Main menu (APPROVED)
**Purpose:** entry point; choose Multiplayer, Deck editor or Solo, reach mod surfaces.
**Layout:** 1920×1080. Top bar 96px tall spanning `left:72 → right:72`: brand "OPTCGSim" 20/500,
`LogPose 2.1` outline tag, spacer, online count with an 8px accent dot (12px glow), 1px×28
divider, profile chip (36×36 avatar r8 + name 15/500 + record 12 mono), 48×48 icon button.
Hero block at `left:72, top:150`: eyebrow 13/600 letterspacing .18em in `#b5abfc`, then
"Where to today?" at 72/500, letter-spacing −.02em.
Destination row at `top:376`, `left/right:72`, `gap:24`, height 392:
- **Multiplayer card** `flex:1.35`, r14, background `linear-gradient(180deg, accent@16%, surface@92%)`,
  `inset 0 0 0 1px accent` + `0 16px 40px rgba(0,0,0,.5)`, padding 32. Kicker 12/600 .1em,
  title 40/500, body 16/400 max-width 420, six format tags (`.tag-neutral`), then two buttons
  56h: "Queue Standard" (primary, hover fill state shown) and "All formats" (secondary).
- **Deck editor card** `flex:1`, surface `#232532`, `0 0 0 1px #3f424d`, padding 32: kicker,
  32/500 title, 16/400 meta, a dashed 1px placeholder for recent-deck leader thumbnails
  (game content), and a 48h secondary button.
- **Solo play card** `flex:1`: same chrome, two 48h dropdowns and a 48h primary "Start match".
Bottom band at `bottom:72`: three 72h utility rows (`flex:1`, r8, surface@70%, 1px edge,
20px Phosphor icon in accent + 17/500 title + 13/400 meta) — Match history, Alt arts, Replays —
then a divider, "Open OPBounty" and "Quit" (48h), and the version string 12 mono at 40% opacity.

Alternative direction `1b` ("Log", flush-left ledger of destinations) is kept in the file for
reference; it is NOT the approved menu.

### 2a — Board in play (core screen)
**Constraint:** zone positions stay familiar. Only chrome, HUD and hand presentation changed.
**HUD bar** `0,0 → 1920×72`, background surface@75%, `inset 0 -1px 0 text@12%`, padding 0 24,
gap 20: opponent avatar 40×40 r8 + name 16/500 + leader 12 mono; LIFE group — 12×20 r3 pips
(`#b5abfc` filled, `#3f424d` empty) with a 16×20 face-up chip (`T`, 1px accent border) and a
"1 face-up" 11px label when a life card is revealed; centered TURN counter 13/600 .14em and
five phase pills 32h (active pill: accent@16% fill, 1px accent, `#d2cefd` text); player LIFE +
identity mirrored right; 48×48 overflow "⋯" button holding the old utility buttons.
**Playmat** `left:72, top:112, 1000×718`, r14, vertical gradient `#1b1d2c → slot → #1b1d2c`,
`0 0 0 1px #3f424d, 0 16px 40px rgba(0,0,0,.45)`, padding 20, column gap 8. Per side, outer→inner:
1. **DON!! cost strip** 86h — flex row: strip `flex:1` (r8, 1px hairline; player strip accent@6%
   fill + accent@35% border) containing seven 54×66 r5 slots (active `#423a6a` + 1px `#796cbf`,
   spent `#292b31`), a left watermark label 12/600 .16em at 14% opacity and a right-aligned
   "5 / 7 ACTIVE" 11 mono counter; then **DECK** then **TRASH** piles, 80×86 r6.
2. **Middle band** — a dedicated **LIFE column** 118 wide (r8, slot fill, 1px hairline, padding
   10 0) holding a "LIFE n" label and n stacked 94×32 r6 cards overlapping −10; beside it a
   `flex:1` column with the leader row and the character strip.
   **Leader row** 130h: STAGE 70×96 r6 and LEADER 96×126 r8 centered (player leader carries
   `0 0 0 1px accent, 0 0 24px accent@35%`); the **opponent hand** is docked here as four
   54×76 face-down cards (rotations −6/−2/2/6, −15px overlap) plus a "HAND 4" label.
3. **Character area** 96h, r8, slot fill, watermark label, five 78×80 r5 slots (occupied slots
   1px edge; the selected one 1px accent).
Center line: 1px accent@50% rule fading to transparent over 100px each end, with a "YOUR TURN"
chip 11/600 .16em on the mat background.
**Player hand** — fanned strip pinned to `bottom:0`, width 1000, cards 120×166 r8 (rotations
±8/±6/±3, −22px overlap); the playable card is 120×182, raised −16px, 1px accent + 28px glow,
with a "PLAYABLE · 3" label. "YOUR HAND 7" label bottom-left.
**Right rail** `left:1104, width 744`: combat log panel `top:104`, 566h, r14, surface, 1px edge —
56h header (label + "Download" + collapse chevron) and log lines as `T4` gutter (11 mono, accent
for the current turn) + 15/400 body, card codes in mono at 50–60% opacity, a fading rule between
turns. Action stack `top:694`: two 56h secondary buttons, then **End turn** 104h r14 primary
(32/500, accent outline, accent@12% fill), then "Report a bug" and a **danger** "Concede match"
(1px `#dd6f5f`, same-color label).

### 2b — Deck editor
88h top bar: back 48×48, deck-name field 48h (1px accent) min-width 360, "51 / 51 · legal for
Standard" (24 mono accent + 15/400 muted), then Load / Import / Alt arts (secondary 48h) and
"Save deck" (primary 48h). Three columns below `top:124 → bottom:36`:
left filter rail 320 (r14 surface panel, padding 24) — search field 48h, Color chips 36h,
Cost chips 36h, three toggles, search-syntax help; center browser — tab row 44h with a 2px
accent underline, result count, then a 7-column grid of 148×207-equivalent card cells
(`grid-template-rows: repeat(4,186px)`, gap 14) with the manifest's scrollbar thumb at the right
edge; right deck panel 500 — leader header (96×134 thumb + name + code + tags), cost-curve
histogram (7 bars, accent ramp, tallest = base accent), 56h deck rows (34×34 thumb, name,
code in mono, "×4" count in accent), footer "Copy list" + danger "Clear deck".

### 2c — Settings
Header at 72/72: back button, eyebrow + "Settings" 56/500, "Restore defaults" secondary.
Three columns from `top:232`, gap 36: **Turn flow** (3), **Combat** (5), and a stacked column of
**Display** (2) + **Privacy** (2). Each section is an r14 surface panel, padding 24, with a
12/600 .12em accent heading; each row is 80h min, r8, ground fill, padding 16 18 — label 17/500,
description 14/400 at 55% (the trigger-leak warning uses `#dd6f5f`), toggle right (52×28 track,
22px knob). Interface scale uses a 4-option segmented control. Version string bottom-left.

### 2d — Multiplayer queues
Header as 2c. Left: six format rows, 960 wide — selected row 104h (accent@10% fill, 1px accent,
26/500 title, queue count 20 mono accent, chevron); others 92h surface rows with 22/500 titles
and 15/400 legality lines; the Private row carries a "Host…" secondary button. Right: 712-wide
r14 panel — format kicker, "Ranked queue" 32/500, explanatory 16/400, fading rule, deck picker
(88×124 leader thumb + 48h dropdown + legality line), then a 72h primary "Enter queue" and an
"Average wait 24s" caption.

### 2e — Solo setup
Back button top-left; centered eyebrow + "Pilot both sides" 56/500 at `top:150`; two 592-wide
r14 seat panels at `top:352` separated by a "VS" 24/500 at 35% opacity, each with a 120×168
leader placeholder, a 56h deck dropdown (P1's is focused: 1px accent) and a 13 mono meta line;
420×72 primary "Start match" and a 48h secondary "Load state from clipboard" centered at
`top:700`; sound/music icon buttons top-right; version bottom-right.

### 2f — Match history (mod surface)
Dimmed menu behind (`rgba(15,17,28,.72)` scrim over the menu at 28% opacity). Modal 1160×888
centered at `top:96`, r14, surface, `0 0 0 1px #9397ab, 0 16px 40px rgba(0,0,0,.65)`.
Header: LogPose kicker + "Match history" 28/500, All/Wins/Losses segmented control, 48×48 close.
Rows 96h r8 ground: 56×78 leader thumb, "A vs B" 20/500 with a 40%-opacity "vs", timestamp +
game + turn count 13 mono, WIN (`.tag-accent`) / LOSS (`.tag-neutral`) pill, 44h "Watch replay"
button (`white-space:nowrap`), opponent leader thumb. Selected row: 1px accent + 3px left mark.
Footer: record summary, Previous / "Page 1 / 1" / Next.

### 2g — Alt art selector (mod surface)
Full-bleed modal inset 60 on all sides, r14, top elevation. Header: kicker + title, helper copy,
300-wide filter field, "Fetch arts" secondary, close. Body splits: 320 card list (72h rows,
36×50 thumb, name 16/500, code + art count 12 mono; selected row 1px accent + 3px mark; pager at
the bottom) and the art pane — title row with a "3 arts available" outline tag, a row of
168×236 art thumbnails (selected: 1px accent + 24px glow and an "IN USE" label), then a
400×472 preview panel beside a details column (art name 20/500, code/rarity 14 mono, fading
rule, explanatory copy, "Use this art" primary + "Reset to default" secondary).

### 2h — Replay viewer (mod surface)
Top bar 72h: REPLAY outline tag, matchup, timestamp, centered "TURN 2 / 20" + "event 169 / 746"
(13 mono accent), "Exit replay" secondary. Playmat `left:72, top:104, 1000×640` — identical
three-band field to 2a at reduced scale (DON!! 76h, leader row 112h, character 86h, life column
104 wide, opponent hand docked beside their leader). Transport panel `top:776, 1000×232`, r14:
turn ruler (T1…T20 12 mono) over a 12h track r6 with a 23% accent-gradient fill, a 24px accent
playhead with a 16px glow and 2px turn ticks; below, transport buttons 52×56 (`|<  <T  <A`,
80×56 primary play/pause, `A>  T>  >|`), a divider, a speed segmented control, and a 13/400
keyboard-hint block right-aligned. Right rail `left:1104, width 744`: event log — past events at
50% opacity, the current event in an r8 accent@12% card with a 1px accent border, upcoming events
at 75%; footer "Jump to hand" / "Download log".

### 1a / 2i — Reference sheets
`1a` is the design-system sheet (palette, ramps, type scale, spacing, radii, elevation and every
component at 1080p size). `2i` draws every 9-slice sprite at its target size. Both are
documentation, not screens to implement.

## Interactions & behaviour
- **States (all interactive elements):** hover = accent fill 12%; pressed = accent fill 22% with
  the border stepping to `#b5abfc`; keyboard/gamepad focus = 2px accent outline at 2px offset;
  disabled = 45% opacity on the whole control (never a different color).
- **Menu:** destination cards raise to the hover fill; the three utility rows behave as buttons.
- **Board:** playable hand cards carry the accent glow and lift 16px; the active phase pill is the
  only filled pill; the combat log rail collapses via the header chevron (rail slides out, mat
  keeps its position — do not reflow the field); "Concede match" and "Clear deck" open a confirm
  modal (`2f`'s modal chrome) since the palette has one danger color and no undo.
- **Deck editor:** filters apply live; the deck panel's selected row mirrors the browser selection.
- **Replay:** the scrubber is draggable; ←/→ steps events, PgUp/PgDn steps turns, Space toggles
  play; the log auto-scrolls so the current event card stays centered.
- **Motion:** 120ms ease-out on hover tints, 180ms on panel slides, 240ms on the card lift. No
  motion longer than 250ms anywhere in the interface.

## State (mod side)
Menu: online count, profile name/record, last-edited deck, patch string. Board: turn number,
phase, both life arrays (with a face-up flag per life card), DON!! active/spent counts, hand
list with a playable flag, log entries keyed by turn, rail-collapsed bool. Deck editor: filter
set, search string, result page, deck map (cardId → count), legality result. Replay: event index,
turn index, play state, speed. Settings: the eleven flags, grouped as in `2c`, plus interface scale.

## Design tokens
Colors, spacing (6px grid), radii (4/8/14), elevation and the full type scale are specified in
`sprite-manifest.md` and drawn in option `1a`. Type: **Inter** 400/500/600 only (convert to TMP
assets; never bolder than 600) with a mono face (**JetBrains Mono**) for card codes and counters.
Minimum on-screen text 16px at 1080p. Screen margin 72, panel padding 24, control gap 12,
group gap 36.

## Assets
- `sprite-manifest.md` — every 9-slice texture with size, radius, slice margins and fills, plus
  the Nocturne→Batsu swap table. Generate the PNGs from this; no gradients baked into 9-slices.
- Icons: **Phosphor** (regular weight), 18–26px in interface chrome.
- Card images, leader art and playmat art: existing game content, untouched. Placeholders in the
  mockups (flat `#2b2d3a` rectangles, sometimes labelled) mark where that content sits.
- No external web assets are used or required at runtime.

## Files
- `OPTCGSim Redesign.dc.html` — Nocturne colorway, all eleven frames (options `1a`–`2i`).
- `OPTCGSim Redesign - Batsu.dc.html` — Batsu brand colorway, identical geometry.
- `sprite-manifest.md` — sprite + token reference.
- `claude-design-brief.md` — the original brief with the modding constraints.

Open either HTML file in a browser; each frame is badged with its option id and drawn at
1920×1080, so measuring directly off the DOM gives the implementation values.
