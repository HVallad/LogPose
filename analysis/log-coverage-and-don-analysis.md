# OPTCGSim Log Coverage & DON!! Tracking Analysis

Based on decompilation of `Assembly-CSharp.dll` from OPTCGSim **1.42a** (Unity 6000.0.58f2, Mono).
Decompiled sources: `decompiled/` (ilspycmd). All line numbers reference `decompiled/GameplayLogicScript.cs` unless noted.

## 1. How the logs work

The game produces **two interleaved streams** into the same `currentCombatLog` list, which is what
gets written to `CombatLogs\AutoSaved\*.log` by `SaveMyLogLines()` (line 5717):

1. **Human-readable lines** — created by `LogLine()` / `LogLineOpponent()` (26515/26534) from ~220
   translation keys (`Log.*`), sent over netcode via `AddLogLineServerRpc` → rendered by
   `AddLocalLogLine()` (5800) with TMP markup (`<mark><link="OP01-001">…`).
2. **Machine-readable replay lines** — the `ReplaySync_*` system (~33080–33478) emits `RZ1|…`
   lines. This is what OPTCGReplay parses.

## 2. RZ1 format specification (reverse-engineered)

```
RZ1|HDR|<gameVersion>|<zoneEnumVersion=2>|RZ1
RZ1|PLY|<playerNum 1|2>|<playerName>|<leaderCardID>
RZ1|<seq>|<playerNum>|<cardID>|<origZone>|<origSlot>|<destZone>|<destSlot>|<visToOwner>|<visToEnemy>|<tapped>|<powDelta>|<costDelta>
RZ1|CHK|<seq>|<playerNum>|<deck>|<hand>|<deploy>|<life>|<donDeck>|<donCostArea>|<trash>|<stage>|<leader>|<equippedDonTotal>
```

Zone IDs (`ReplaySyncZone.cs`):

| ID | Zone | ID | Zone |
|----|------|----|------|
| 0 | Deck | 5 | DON cost area |
| 1 | Hand | 6 | Trash |
| 2 | Character area | 7 | Stage |
| 3 | Life | 8 | Leader |
| 4 | DON deck | 9 | DON equipped |

Zone 9 slot encoding (`EncodeDonEquippedSlot`): `slot = parent * 100 + attachIndex`, where
`parent = 99` for the leader, otherwise the deploy index of the character it's attached to.

Key emit facts:
- A move line is emitted by `ReplaySync_EmitMove` (33377) and is always followed by a `CHK` line.
- `EmitCurrentZoneState` (33422) re-emits a card *in place* (same zone/slot) — used to publish
  state changes without movement (life flips, reveals). **The `tapped` field rides along on every
  move line** — this is the only way tap state reaches the stream.
- `powDelta` is computed with **`bIgnoreDon: true`** (33318) — DON attachment power (+1000/don)
  is *never* included in powDelta. A replayer must count zone-9 attachments itself.
- `CHK` carries only the **total** equipped-don count per player (33372), not per-card counts.

## 3. What IS tracked correctly (DON!!)

All DON **zone movements** emit properly:

| Event | Method (line) | Notes |
|-------|---------------|-------|
| Draw DON (refresh) | `DrawDon_Internal` (21950) | zone 4→5 |
| Draw rested DON (effects) | `DrawRestedDon_Internal` (22022) | arrives tapped=1 ✓ |
| Return DON to don deck | `ReturnDon_Internal` (22061) | zone 5→4 |
| Attach DON (manual/effect) | `AttachDonToCard` (8325), `AttachRestedDon_Internal` (9417) | zone 5→9 |
| Detach DON → cost area | `DetachSpecificDon` (6981) | zone 9→5, arrives **tapped=1** |
| Return DON from card → don deck | `ReturnDonFromCard` (8343) | zone 9→4 |
| Transfer DON between cards | `TransferAttachedDon_Internal` (9447) | zone 9→9 |
| Rest/activate cost-area DON **via effects** | `TapDon_Internal` (24399) / `UntapDon_Internal` (24363) | in-place emit per don ✓ |

## 4. What is NOT tracked — the gaps

### 4.1 THE BIG ONE: refresh phase is silent — `PlayerUntap` (7144)

At the start of every turn, `PlayerUntap`:
1. Returns the leader's attached DON via `ReturnCardDon` → **emitted** (each arrives in cost area
   as `tapped=1` because `DetachSpecificDon` line 6985 rests them).
2. Returns each character's attached DON → **emitted**, same rested state.
3. Untaps leader (7163), all characters (7187), **all cost-area DON (7201)**, stage (7213) by
   writing `bTapped = false` directly — **zero RZ1 lines emitted**.

**Net effect on a replayer:** every DON returned from leader/characters is recorded entering the
cost area *rested*, and the wholesale untap that follows is invisible. Rested DON from last turn's
costs also silently become active. From turn 2 onward the replay's DON rest-state is wrong and
stays wrong until each individual don happens to move again. **This is almost certainly why
OPTCGReplay "struggles with showing the right don."** The same applies to leader/character/stage
rest states.

### 4.2 Attack/battle rest states are silent

- `StartAttack` (24786) rests the attacker via `TapCard` → `TapCard_Internal` (12491) — **no emit**.
- `SetBlocker` (12405) rests the blocker directly (12408) — **no emit**.
- Effect-driven rest/activate of leader/characters goes through `TapCard_Internal` /
  `UntapCard_Internal` (12611) — **no emit** (note: the human log DOES record these via
  `Log.SetOtherRest`, `Log.SetActive`, etc., but RZ1 does not).

### 4.3 Other silent state changes

| What | Where | Impact |
|------|-------|--------|
| Stage tap/untap | `TapStageCard_Internal` (10902), `UntapStage_Internal` (12634) | rest state wrong |
| DON/card "freeze" (skip next refresh) | `FreezeDonCard_Internal` (12551), `FreezeCard_Internal` (12588) — sets `bSkipNextActive` | replayer can't predict which don stay rested at refresh; becomes consistent once refresh emits (see fix) |
| Untap on leaving field | `ResetCardEffects` (10926) untaps *after* the move already emitted with the old tapped bit | cosmetic only (hand/trash) |
| Power/cost deltas exclude DON | `ReplaySync_ComputePowerCostDelta` (33309), `bIgnoreDon: true` | replayer must add +1000 × attached don itself |
| Turn/phase boundaries | no RZ1 record at all | replayer must infer turns from DON draws / untap patterns |
| Counter values during battle | human log only (`Log.Counter`, `Log.UseCounter`) | battle math not in RZ1 |

### 4.4 Human-readable log gaps (for completeness)

The 220 `Log.*` keys cover deploys, attacks, blockers, counters, life, DON effects, reveals, and
most named effects. Not present in the human log:
- Manual tap/untap of own cards outside effects (no key fires from `TapCard`/`UntapCard` RPC paths).
- Refresh-phase summary ("returned N don, untapped M cards" is never printed).
- Deck/hand reorders (RZ1 has `EmitReorderedZone`; the human log says nothing).
- Encoding bug: log files are written by `StreamWriter` default encoding while names contain
  zero-width characters — produces mojibake like `Nickgadabaâ€‹#4387` in saved files.

## 5. Fix strategy (implemented in the OPSimExtensions plugin)

The stream can be made complete **without changing the RZ1 schema** — the replayer keeps working,
it just finally receives the missing lines:

| Patch | Method patched | Effect |
|-------|----------------|--------|
| Postfix | `PlayerUntap` | after vanilla untap, `EmitCurrentZoneState` for leader, every character, stage, and **every cost-area DON** → publishes tapped=0 with correct zones |
| Postfix | `TapCard_Internal`, `UntapCard_Internal` | emit the affected leader/character in place |
| Postfix | `TapStageCard_Internal`, `UntapStage_Internal` | emit stage card in place |
| Postfix | `SetBlocker` | emit the blocker in place (tapped=1) |
| Postfix | `FreezeDonCard_Internal` | re-emit affected don so the freeze-caused "stays rested" is at least consistent at next refresh |

All patches call the game's own private `ReplaySync_EmitCurrentZoneState`, so sequence numbers,
CHK lines, and formatting stay canonical. Games between one modded and one unmodded client stay
compatible (RZ1 lines are generated locally on each client from synced state).

## 6. Reference: emit call sites

`ReplaySync_AfterMutation`/`EmitMove` call sites confirmed at: 6990, 8339, 8357, 9428, 9460,
10968, 11020–11475 (hand/deck/trash moves), 13771–13817, 21265–21746, 21962, 22035, 22072,
22868, 24375–24429, 24501–24676, plus `EmitReorderedZone` at 22118 (hand) and 22184 (deck).
Tap-state writes with **no** emit: 7163, 7187, 7201, 7213, 10904, 10929, 12408, 12493, 12613, 12638.
