# Game Logic Blueprint

This document is the current blueprint for implementing the escape-room game logic in the Unity project.

It captures:
- current scene/object inventory
- object categories and gameplay roles
- intended clue/decoy flow
- timer, score, and hint rules
- controller/state responsibilities
- implementation guidance
- unresolved items that still need authoring or iteration

This is intended to be the shared reference for teammates implementing game systems, clue objects, UI, and sequencing.

## 1. High-Level Game Concept

The player explores a retro room in VR and must solve a sequence of clues before time runs out.

There are three major classes of objects:

1. `Real clues`
- interactable
- progress the game
- count toward score and final reveal

2. `Decoy clues`
- interactable
- look meaningful
- temporarily mislead the player
- do not progress the game

3. `Distractor objects`
- non-interactable
- environmental clutter / noise
- do not help solve the game

In practice, there is also a fourth class that matters:

4. `Cover objects`
- interactable, but not clues themselves
- can hide clues behind / under / inside them
- examples: clocks, couches, large furniture, door panels, etc.

## 2. Main Intended Gameplay Loop

Current intended player journey:

1. Find the first clue in plain sight on a sofa / seat area.
2. Interpret it and search behind the wrong couch first (due to lighting + decoy).
3. Find a decoy clue and briefly follow it.
4. Realize it is a decoy and return to the clue path.
5. Search behind the correct couch and find the real visual clue.
6. Use that clue to identify the correct corner table.
7. Read the note at the correct corner table.
8. Go to the clock wall.
9. Interact with the clock.
10. Reveal the final key behind the clock.
11. Bring the key to the door.
12. Door opens, timer stops, score is calculated.

## 3. Current Scene Inventory

### Already implemented / physically working

#### `Clock_Wall12`
- Type: wall-mounted interactable
- Current behavior: proximity + grip tilt
- Current script: `Assets/Scripts/ClockProximityTilt.cs`
- Planned role: final reveal container for the real key

#### `TableProp_Book1`
- Type: grabbable portable object
- Current behavior: `XR Grab Interactable`
- Current location: moved onto sofa
- Planned role: likely real readable clue behind the correct couch

#### `TableProp_Keys`
- Type: grabbable portable object
- Current behavior: `XR Grab Interactable`
- Current role in existing prototype: key used on the door
- Planned role: final key, hidden behind the clock until clue chain is completed

#### `Door_Wall16`
- Type: wall-mounted hinged exit
- Current behavior: opens via `DoorProximityHinge` when keys enter knob trigger
- Current script: `Assets/Scripts/DoorProximityHinge.cs`
- Current setup helper: `Assets/Editor/DoorWall16Setup.cs`
- Current special behavior: stays open once triggered (latched success)

### Non-interactable distractor clutter added

- `FloorDistractor_Book_A`
- `FloorDistractor_Book_B`
- `FloorDistractor_CD_A`
- `FloorDistractor_CD_B`
- `FloorDistractor_Keys_A`

These are intended to remain non-solution objects.

### Larger room objects relevant to puzzle design

- multiple single-seater sofas / chairs
- two larger couches
- corner tables
- wall clocks
- room furniture and decor

These large floor-mounted objects are intended to be treated as `cover objects`.

## 4. Current Object Role Plan

### Real clue objects

Planned real progression:

1. `Clue1_PlainSight`
- a new note / card / mesh
- placed on a sofa arm / back edge
- text clue

2. `Clue2_BehindCouch`
- physical object hidden behind the correct couch
- currently expected to use `TableProp_Book1`
- not text-only; ideally a visual clue

3. `Clue3_CornerTable`
- readable note associated with the correct corner table
- likely placed under the globe
- text clue pointing toward the clock

4. `Clock_Wall12`
- final reveal clue container
- once the required prior clues are solved, tilting it reveals the final key

### Decoy clue objects

Planned decoy structure:

#### Decoy branch 1: wrong couch
- placed behind / around the wrong lit couch
- looks like the correct second clue at first
- once followed to the next zone, that next zone reveals `DECOY`

#### Decoy branch 2: wrong corner table
- likely associated with CD / circular-object interpretation
- note or clue-like object is present
- also resolves explicitly as `DECOY`

Important rule:
- only one decoy branch at a time
- decoys never chain into more decoys
- the player should only lose one step before learning it is false

### Distractors

All current floor distractors and general clutter remain:
- non-interactable
- non-solution
- purely environmental

### Cover objects

All large floor-mounted objects that can plausibly hide clues should eventually be interactable.

Examples:
- couches
- large sofas
- clocks
- possibly tables or other large moveable furniture

These are not clues themselves, but they can conceal clue objects.

## 5. Intended Final Puzzle Mapping

### Sofa / couch zone

#### Real clue 1
- `Clue1_PlainSight` is placed on the arm or top edge of a single-seater sofa
- should face the player's natural early view / spawn scanning direction
- visible enough to discover early, but small enough to require inspection

#### Lighting misdirection
- a different couch / sofa should be emphasized with a point light
- this is intended to attract the player to the wrong couch first

#### Decoy near wrong couch
- a valid decoy clue should be placed behind / near the wrong lit couch
- it should feel like the second clue at first
- it should eventually reveal itself as `DECOY`

#### Current decoy support object
- `FloorDistractor_Keys_A` can remain under the wrong sofa/couch area as environmental support for the misread

### Behind-the-couch zone

#### Real clue 2
- use `TableProp_Book1`
- place it behind the correct large couch
- on the floor near the center
- slightly reachable / visible once the player checks the correct couch

#### Clue 2 format
- should not be text-based if possible
- recommended: a visual sketch / diagram
- recommended direction: corner table with globe / world icon

### Corner table zone

#### Real clue 3
- note associated with the correct corner table
- ideally placed under the globe
- should point to the clock

#### Decoy corner table
- another corner table should contain a misleading clue-like object
- CD / circular object ambiguity is intentional
- the next interpreted zone should reveal `DECOY`

### Clock wall zone

#### Clock
- `Clock_Wall12` is the final clue container
- should remain interactable throughout
- key behind clock is not available until the required clue chain is complete

#### Final key
- `TableProp_Keys`
- hidden behind or just under the clock
- initially disabled / hidden
- revealed only after all required real clues are solved

#### Door
- `Door_Wall16`
- final objective
- bringing final key to the knob trigger opens the door

## 6. Current Clue Content Direction

### Clue 1 (text)
Status: not finalized

Current direction:
- should imply `behind the couch`
- should ideally imply a large couch rather than any seat

Candidate tone:
- short
- poetic but understandable
- not too vague

### Clue 2 (visual)
Status: not finalized

Current direction:
- no text
- should visually imply:
  - corner
  - table
  - globe / world / round object

Recommended asset:
- note card / page / sketch

### Clue 3 (text)
Status: not finalized

Current direction:
- point from the correct corner table toward the clock / time / wall clock

### Clock reveal text
Status: optional, not finalized

Possible back-of-clock or reveal text:
- `Behind the face, freedom.`

### Decoy text behavior
Status: not finalized

Required behavior:
- decoy appears valid at discovery
- after player follows it to the next zone, the clue or zone reveals `DECOY`
- player must return to actual clue path

## 7. Timer, Score, and Hint Rules

### Timer
- total time: `10 minutes`
- starts at game start
- displayed in the collapsed hint menu / HUD
- stops when:
  - door opens successfully, or
  - time reaches zero

### Score formula
- `+1` for each real clue solved
- if the player finishes the game by unlocking the door:
  - `+1` for every full minute left
- `-0.25` for each hint used

Current working formula:

`score = solvedRealClues + minutesRemainingIfCompleted - (0.25 * hintsUsed)`

Notes:
- score can be stored as `float`
- time bonus applies only if the game is completed

### Hint rules
- total hints allowed: `2`
- one hint is real
- one hint is decoy / misleading
- score penalty per hint: `-0.25`
- hints should be visible / managed through the same compact HUD/menu area as timer and score

### HUD (collapsed hint menu area)
Should display:
- `Time Left`
- `Score`
- `Hints Remaining`

Optional later:
- `Clues Solved X/N`

## 8. Endgame Rules

### Success
- triggered when final key opens the door
- timer stops
- final score is computed
- success message is shown

### Failure
- triggered when timer reaches zero
- failure message is shown
- score is shown

Planned later polish:
- lighting up missed clues
- clue reveal summary
- answer / path explanation

## 9. State / Sequencing Direction

The current state model is intentionally lightweight for now and can be iterated later.

### Current working flags
- `clue1Solved`
- `clue2Solved`
- `clue3Solved`
- `clockRevealSolved` or equivalent
- `finalKeyRevealed`
- `doorOpened`
- `timeExpired`
- `gameCompleted`
- `hintsUsed`

Optional tracking flags:
- `decoy1Found`
- `decoy2Found`

### Important current rule
- key reveal should happen only when the required clue chain is complete
- bringing key to door is the final trigger

## 10. Controller Architecture Direction

### Object-level controllers
Each interactable object should control only its own state and behavior.

Examples:
- clue object knows if it has been read / solved
- cover object knows if it has been moved / opened
- clock knows whether it is open / tilted
- door knows whether it is open / closed / unlocked

These object-level controllers should be state-aware but not responsible for full game sequencing.

### Game-level controllers
Game-wide systems should handle sequencing and scoring.

Recommended managers:
- `PuzzleManager` or `GameFlowController`
- `ScoreManager`
- `TimerManager`
- `HintManager`
- `UIStateController`

Responsibilities:
- track solved clue count
- manage decoy branch state
- reveal final key
- stop timer
- calculate score
- trigger end states

### Suggested design principle
- object controllers emit events
- game controller listens and decides progression

Examples:
- clue emits `Solved`
- decoy emits `Triggered`
- cover object emits `Opened` / `Moved`
- game controller reacts and updates global state

## 11. Existing Technical Infrastructure

### Clock
- file: `Assets/Scripts/ClockProximityTilt.cs`
- current interaction: near controller + grip to tilt
- current note: default collider was recently enlarged slightly for easier triggering

### Door
- file: `Assets/Scripts/DoorProximityHinge.cs`
- setup tool: `Assets/Editor/DoorWall16Setup.cs`
- current behavior:
  - opens slowly
  - wall is hidden so the door visually replaces `Wall (16)`
  - knob trigger is hidden
  - once unlocked, door stays open

### Current grabbables
- `TableProp_Book1`
- `TableProp_Keys`

These already have working grab setups in the scene.

## 12. Implementation Rules To Keep Consistent

1. Real clues should increment progress and score.
2. Decoys should never increment progress.
3. Decoys should mislead only one step, not chain.
4. Large floor-mounted objects should be interactable if they can plausibly hide clues.
5. Distractor objects should remain non-interactable.
6. The final key should not appear until the clue chain is completed.
7. The clock stays interactable even before the key is revealed.
8. Door opening ends the round.

## 13. What Still Needs To Be Finalized

These are not fully finalized yet and still require explicit authoring decisions.

### A. Exact object placement mapping
Need to explicitly lock:
- which single-seater sofa gets clue 1
- which couch is the wrong lit couch
- which couch is the correct real couch
- which corner table is the real globe table
- which corner table is the decoy table
- exact final key placement behind/under clock

### B. Exact authored clue content
Need to finalize:
- exact text of clue 1
- exact visual design of clue 2
- exact text of clue 3
- exact decoy message wording
- exact clock reveal wording (if any)

### C. Final clue count convention
This is still slightly ambiguous in language.

Possible interpretations:
- `3 real clues` before final key reveal
- or `4 real interactions` if the clock itself is counted as a clue stage

Team should decide which numbering convention to use in code/UI.

Recommended practical structure:
- clue 1: sofa note
- clue 2: behind couch visual clue
- clue 3: corner table note
- clock reveal: final reveal stage

### D. How clue reading/inspection works
Need to decide:
- pickup to read?
- UI popup?
- auto-display when close?
- dedicated inspect interaction?

This especially matters for:
- clue notes
- decoy clues
- visual clue cards

### E. Cover object interaction styles
Not all large cover objects should necessarily behave identically.
Need to define per object whether they:
- grab and move
- slide
- hinge open
- tilt
- toggle / reveal

### F. Timeout presentation
Need to finalize:
- exact failure message
- whether player can continue looking after timeout
- whether score screen is modal

### G. Leaderboard
Known future requirement but not implemented.
Still needs decisions on:
- local vs persisted
- data format
- name entry
- what qualifies as leaderboard entry

## 14. Recommended Immediate Next Steps For Teammates

1. Finalize clue text and clue art/content.
2. Lock exact room-object mapping for sofa/couch/corner-table placements.
3. Implement the global game controller layer:
   - timer
   - score
   - clue progression
   - key reveal
   - end state
4. Implement clue object controllers for:
   - real clue
   - decoy clue
   - cover object
5. Wire UI for:
   - timer
   - score
   - hint count
6. Connect final key reveal to clue completion state.
7. Connect door open to success end state and score finalization.

## 15. Short Summary

Current prototype already has:
- a working clock interaction
- a working door interaction
- working grabbable book and keys
- non-interactable distractor clutter

The main remaining work is not basic interaction anymore.
The remaining work is:
- clue authoring
- state/sequencing
- UI/timer/score/hints
- final puzzle wiring

