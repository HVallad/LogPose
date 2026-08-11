# UI redesign brief — OPTCGSim (One Piece TCG simulator)

Attached are 9 screenshots of every major screen. I want a full visual redesign of this game's UI.

## Context

OPTCGSim is a fan-made Unity simulator for the One Piece Card Game. I don't have the game's
source — I restyle it at runtime through a BepInEx mod I maintain (LogPose). That gives me a
lot of power, but it shapes what a design can ask for:

**I can:** replace any sprite (9-slice PNGs loaded from disk), swap fonts (any free-license
TTF, converted to TextMeshPro), recolor anything, move/resize/re-anchor any UI element,
hide elements, and build entirely new panels/screens from scratch in code.

**I can't:** use external web assets at runtime, bake text into images (must stay real text
for localization/scale), or change the card images and playmat art (that's licensed game
content the sim ships).

## The screens (attached, 1-9)

1. **Main menu** — parchment buttons floating on a wood table. No hierarchy, no branding,
   buttons in three loose columns with inconsistent sizes.
2. **Multiplayer queue select** — six identical parchment slabs with caption text floating
   under them.
3. **Settings** — raw checkboxes with description text in a second column; reads like a
   debug panel.
4. **Deck editor** — the busiest screen: deck grid top-right, card browser bottom-right,
   controls and help text dumped down the left column. Filter checkboxes, a search box, and
   third-party buttons all compete. (The "Alt Arts" button is from my mod.)
5. **Solo setup** — two dropdowns and a Start button scattered in empty space.
6. **Board, in play** — the core experience. Center playmat with zones, hands rendered as
   card strips top-left/bottom-left, combat log panel mid-left, action buttons ("End Turn",
   "Mulligan"/"Keep") bottom-right, utility buttons top-right. Turn indicator is plain text.
7. **Match History** (my mod, follows the vanilla style) — list rows with leader thumbnails
   and WIN/LOSS.
8. **Alt Art Selector** (my mod) — rows of card thumbnails with a green selection frame and
   hover-to-enlarge.
9. **Replay viewer** (my mod) — the board plus a transport panel (step/turn/action buttons,
   play/speed) bottom-right and a synced log.

## What's wrong

Everything is default-Unity-with-a-parchment-texture: one button style stretched to every
size, default font at wildly different scales, no spacing system, no states (hover is a
faint tint), no hierarchy between primary actions and utilities, huge dead space on some
screens while the deck editor is cramped. It's functional and everyone tolerates it — but it
looks like a debug build.

## What I want from you

1. **A design system first.** Palette (hex values; the game must keep reading "One Piece" —
   adventurous, nautical, warm — but modern and premium, not beige parchment on wood).
   Typography: recommend 1-2 free-license TTFs (Google Fonts is fine) with a type scale.
   Spacing/radius/elevation rules. Component specs with exact px at 1920x1080 reference
   resolution: primary/secondary/danger buttons (normal/hover/pressed/disabled), panels,
   list rows, checkboxes as toggles, dropdowns, text inputs, tabs, tooltips, modals.
   The quality bar: premium digital TCG — think MTG Arena / Legends of Runeterra menus.

2. **Per-screen redesigns** as static, self-contained HTML mockups at 1920x1080, one per
   screen, in this order (stop for my approval after the first): main menu, board in play,
   deck editor, settings, multiplayer queues, solo setup, match history, alt art selector,
   replay transport. Reuse the design system rigorously — I will turn these into Unity
   layouts by hand, so exact spacing/sizes/colors in the markup matter more than clever CSS.

3. **A sprite manifest.** Every 9-slice texture the system needs (panel background, button
   states, input field, toggle track/knob, scrollbar, row highlight...), each with target
   size, corner radius, slicing margins, and colors — so I can generate the PNGs.

## Constraints that matter

- Board zone LAYOUT must stay familiar (players know where hands/deck/trash/DON live) —
  restyle the chrome, reposition HUD elements if it helps, but don't reinvent the table.
- Everything must scale 1080p to 4K: 9-slice sprites, real text, no baked-in labels.
- Card thumbnails and playmats are fixed content the design must sit around.
- The mod adds surfaces (7-9) that should feel native to the new system, not bolted on.
- Dark-friendly: long play sessions, so avoid searing brightness; but this is a cheerful
  pirate adventure game, not a horror UI — keep it warm.

Start with the design system and the main menu mockup. Ask me anything that's ambiguous
before locking the palette.
