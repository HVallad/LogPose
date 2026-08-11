# OPTCGSim — sprite manifest (Nocturne)

Authored at 1080p size, exported ×2 for 4K. No gradients baked into 9-slice textures
(so Unity can tint per state); shadows and glows are separate additive sprites.
Slice margins are **L / T / R / B**.

## Tokens referenced

| Token | Hex |
| --- | --- |
| ground | `#161826` |
| surface | `#232532` |
| surface-raised | `#2b2d3a` |
| edge (hairline) | `#3f424d` |
| edge-strong | `#595d6c` / `#9397ab` |
| text | `#e9e9ed` |
| text-muted | `#e9e9ed` @ 55% |
| accent | `#9184d9` |
| accent-400 (pressed border) | `#b5abfc` |
| accent-300 (text on tint) | `#d2cefd` |
| danger | `#dd6f5f` |
| presence (hero field) | `#262a60` → `#353b80` |

Radii: 4 / 8 / 14. Grid: 6 px. Screen margin 72. Panel padding 24.

## Sprites

| Name | Texture | Radius | Slice L/T/R/B | Fill | Edge |
| --- | --- | --- | --- | --- | --- |
| panel_surface | 64×64 | 14 | 18/18/18/18 | `#232532` | 1px `#3f424d` |
| panel_modal | 64×64 | 14 | 18/18/18/18 | `#232532` | 1px `#9397ab` |
| modal_scrim | 8×8 | 0 | — | `#0f111c` @72% | — |
| btn_primary_normal | 48×48 | 8 | 12/12/12/12 | transparent | 1px `#9184d9` |
| btn_primary_hover | 48×48 | 8 | 12/12/12/12 | `#9184d9` @12% | 1px `#9184d9` |
| btn_primary_pressed | 48×48 | 8 | 12/12/12/12 | `#9184d9` @22% | 1px `#b5abfc` |
| btn_secondary_normal | 48×48 | 8 | 12/12/12/12 | transparent | 1px `#e9e9ed` @16% |
| btn_secondary_hover | 48×48 | 8 | 12/12/12/12 | `#e9e9ed` @7% | 1px `#e9e9ed` @28% |
| btn_secondary_pressed | 48×48 | 8 | 12/12/12/12 | `#e9e9ed` @14% | 1px `#e9e9ed` @36% |
| btn_danger_normal | 48×48 | 8 | 12/12/12/12 | transparent | 1px `#dd6f5f` |
| btn_danger_hover | 48×48 | 8 | 12/12/12/12 | `#dd6f5f` @14% | 1px `#dd6f5f` |
| input_field | 40×40 | 8 | 10/10/10/10 | `#232532` | 1px `#e9e9ed` @16% |
| input_field_focus | 40×40 | 8 | 10/10/10/10 | `#232532` | 1px `#9184d9` |
| row_default | 32×32 | 8 | 9/9/9/9 | `#161826` on panel · `#232532` on ground | none |
| row_hover | 32×32 | 8 | 9/9/9/9 | `#9184d9` @8% | none |
| row_selected | 32×32 | 8 | 9/9/9/9 | `#9184d9` @10% | 1px `#9184d9` + 3px left mark `#9184d9` |
| toggle_track_on | 52×28 | 14 | 14/14/14/14 | `#9184d9` @22% | 1px `#9184d9` |
| toggle_track_off | 52×28 | 14 | 14/14/14/14 | `#292b31` | 1px `#e9e9ed` @16% |
| toggle_knob | 22×22 | circle | — | `#9184d9` on · `#75798c` off | none |
| tab_underline | 8×2 | 0 | 2/0/2/0 | `#9184d9` | — |
| scrollbar_track | 12×24 | 6 | 6/6/6/6 | `#292b31` | none |
| scrollbar_thumb | 12×24 | 6 | 6/6/6/6 | `#595d6c` (hover `#75798c`) | none |
| zone_slot | 32×32 | 8 | 9/9/9/9 | `#161826` @35% | 1px `#e9e9ed` @10% |
| zone_slot_active | 32×32 | 8 | 9/9/9/9 | `#9184d9` @6% | 1px `#9184d9` @35% |
| card_slot_empty | 32×32 | 6 | 8/8/8/8 | `#2b2d3a` @35% | 1px `#e9e9ed` @8% |
| card_frame_selected | 32×32 | 6 | 8/8/8/8 | transparent | 1px `#9184d9` |
| don_active | 32×32 | 5 | 7/7/7/7 | `#423a6a` | 1px `#796cbf` |
| don_spent | 32×32 | 5 | 7/7/7/7 | `#292b31` | none |
| life_pip | 12×20 | 3 | — | `#b5abfc` filled · `#3f424d` empty | none |
| life_pip_revealed | 16×20 | 3 | 5/5/5/5 | `#2b2d3a` (card-face stand-in) | 1px `#9184d9` — used when the top life card is face-up; label the count beside the row |
| tooltip_bg | 40×40 | 8 | 10/10/10/10 | `#232532` | 1px `#9397ab` |
| tag_bg | 24×24 | 6 | 7/7/7/7 | `#423a6a` accent · `#3f424d` neutral | none |
| rule_fade | 256×1 | — | stretch center only | `#e9e9ed` @16%, alpha ramp 48px each end | — |
| glow_accent | 128×128 | — | none (additive) | radial `#9184d9` @55% → 0 | — |
| hud_bar | 8×72 | 0 | 0/0/0/8 | `#232532` @75% | 1px bottom `#e9e9ed` @12% |
| hero_field | 128×128 | 0 | none | linear `#262a60` → `#161826`, plus radial `#353b80` @85% | — |

## Notes for the mod

- Focus ring is drawn, not a sprite: 2px `#9184d9` outline at 2px offset.
- Disabled = 45% alpha on the whole control; never a separate texture.
- Elevation is `1px edge + ambient darkness`: `panel_surface` for md, `panel_modal` + scrim for lg.
- Text minimum 16px at 1080p; card codes and counters in the mono face.

## Batsu colorway (alternate set)

Same sprites, same sizes and slicing — only the fills change. Swap these hexes and the
manifest above applies unchanged.

| Role | Nocturne | Batsu |
| --- | --- | --- |
| ground | `#161826` | `#17121e` |
| surface | `#232532` | `#241d2e` |
| surface-raised | `#2b2d3a` | `#2e2639` |
| edge hairline | `#3f424d` | `#473c52` |
| edge strong | `#595d6c` / `#9397ab` | `#655770` / `#a294ab` |
| text | `#e9e9ed` | `#f0e9f2` |
| accent | `#9184d9` | `#d81fb4` (brand magenta) |
| accent pressed border | `#b5abfc` | `#ea55c8` |
| accent text on tint | `#d2cefd` | `#f7a8e4` |
| DON!! active | `#423a6a` + `#796cbf` | `#5a1149` + `#b81c98` |
| secondary / hero field | `#262a60` → `#353b80` | `#0e3b52` → `#14607f` (brand cyan) |
| danger | `#dd6f5f` | `#dd6f5f` (unchanged) |
