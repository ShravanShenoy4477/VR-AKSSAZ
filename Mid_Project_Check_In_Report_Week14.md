# Mid-Project Check-In Report (Week 14)

**Course:** CSCI 538 — VR Game Development  
**Project:** VR Escape Room (single-room, Meta Quest)  
**Deliverable:** Mid-Project Check-In (target length: 1–2 pages)  
**Submitted:** April 22, 2026  
**Platform / stack:** Meta Quest (Android), Unity, XR Interaction Toolkit  

*Authors / team:* *[Add names here]*  

---

## Executive summary

Playtesting on Meta Quest validated the core puzzle loop (clue discovery → sofa reveal → couch book → globe → clock → key → door), grip-based interaction consistency, and HUD urgency. Several high-severity issues were fixed during iteration (key bypass, decoy false triggers, clue UI layout). Two **critical** items remain: **Play Again** on the time-up screen (blocked by `Time.timeScale = 0`) and **door/key wiring** in the Inspector. Planned work now focuses on decoy rule consistency, cover-object interactions, scoring/hint tuning, and endgame messaging polish.

---

## 1. Playtest results (summary)

**Method:** On-device sessions on Meta Quest; testers given minimal instruction to approximate first-time use. Focus: puzzle flow, interaction feel, UI readability, and decoy behavior.

**What worked well**

- **Consistent input:** Grip-based patterns across sofa, globe, books, and gramophone felt learnable after the first clue.
- **Clue presentation:** Headset-facing clue cards with a short enforced read delay improved comprehension; visual distinction (e.g., warm real clues vs. silver CD-style decoy art) helped signal authenticity without heavy tutorial text.
- **Flow:** Players who found Clue 1 generally progressed to the sofa slide, couch book, and globe; globe spin was described as satisfying.
- **Timer HUD:** Floating countdown was noticeable but not overwhelming; red state in the final minute increased tension appropriately.

**Issues observed (representative)**

- **Decoy misfire:** Gramophone decoy fired when grabbing Clue 1 due to an oversized overlap zone — **addressed** by tightening checks (e.g., direct distance ~0.35 m vs. loose trigger volume).
- **Puzzle skip:** Key visible at start allowed bypass — **addressed** by hiding the key until `PuzzleManager` confirms all three clues solved.
- **Clue 1 readability:** Note on the moving book surface was hard to read — **addressed** by headset popup consistent with other clues.
- **Failure UI:** **Play Again** on the time-up screen did not restart reliably when `Time.timeScale = 0`; mitigation attempted, **fix still in progress** (see bugs).

*Note:* The narrative below reflects the **current playable build** as documented in the Week 14 check-in and includes the team’s finalized near-term refinements for implementation.

---

## 2. Bug list and prioritization

Top priority issues are tracked below to keep this report concise:

- **P0 (Critical) — Play Again non-functional (In progress):** Time-up screen restart is unreliable because `Time.timeScale = 0` interferes with current button/input path.
- **P0 (Critical) — Key ↔ door not wired (Open):** `DoorProximityHinge` must reference the correct key object in Inspector or the win condition cannot complete.
- **P1 (High) — Decoy false trigger (Fixed):** Gramophone decoy previously fired while grabbing Clue 1 due to overlap zone sizing.
- **P1 (High) — Physics/interaction regressions (Fixed):** Includes camera fling on clue grab and sofa collision trigger misconfiguration.

Other lower-priority fixed items (UI text overflow, clue canvas scale, key-at-start visibility, placeholder hints) are closed and available in internal dev notes.

---

## 3. Planned refinements

**Immediate (blocking final polish)**

1. **Resolve Play Again:** Use input/scene reload path that works at `timeScale = 0` (e.g., `Time.unscaledDeltaTime`, non-physics raycasts, or explicit XR UI event path), or reset time scale before reload.
2. **Verify door pipeline:** Assign key reference on `DoorProximityHinge`; smoke-test win path end-to-end on device.
3. **Lock puzzle sequencing rules:** Finalize clue order, decoy constraints (**at most one active decoy branch**, explicit **DECOY** resolution in the next zone, no chaining), and whether the **clock** is only a reveal container or also awards a “clue solved” for scoring.

**Gameplay and UX**

4. **Clock clue bridge:** Add a headset-facing clock clue after globe / prior chain, consistent with other popups, gated by progression (matches blueprint: clock remains interactable; key appears only after real clues).
5. **Misdirection lighting:** Warm point/spot on the **wrong** couch to pull attention before the real behind-couch clue (blueprint + open bug).
6. **Dark-room lighting pass:** Consider a significantly darker room baseline with intentionally low-lit clue pockets for both real and decoy paths; validate readability/comfort on Quest.
7. **Spatial sound guidance:** Add subtle directional audio hints that increase as players approach both real clues and decoy clues (distinct but consistent sound language).
8. **Limited spotlight assist:** Add a player-held spotlight with limited uses/charges; base room lighting remains adequate, but spotlight raycast can briefly “x-ray” or highlight potential clues behind cover objects.
9. **Locomotion pass:** Reduce drift and teleport edge cases; comfort pass for full room traversal.
10. **Interaction consistency:** Normalize proximity thresholds, popup timing, and “GOT IT” dismiss behavior across all clue scripts.

**Door and props**

11. **Door readability:** Consider a visible keyhole or clearer receptor (blueprint: key-to-knob contact is acceptable; docx suggests more physical affordance — pick one and standardize colliders).
12. **More interactables + timed distractors:** Add additional interactable props and timed distraction events (e.g., table object fall, temporary disappearance of an easy clue) while preserving fair puzzle solvability.

**Systems (per blueprint)**

13. **Score + timer + hints:** Keep HUD compact: **Time left**, **Score**, **Hints remaining**; formula: **+1 per real clue**, **+1 per full minute left if door unlocked**, **−0.25 per hint**; success and failure screens both show final score.
14. **Progress-aware hinting:** Upgrade hint menu from static hints to state-aware guidance based on current progression (between-clue directions, decoy recovery prompts, and next best objective).
15. **Cover objects:** Make large floor-mounted “hiding” furniture interactable (move/slide/tilt) where clues can be concealed — track per-object interaction style.
16. **Multi-room branch extension:** Expand from single-room to a branch point after early puzzles where one next room offers a shorter path and another offers a longer, clue-heavy path.
17. **Dynamic difficulty assist:** If the player is stuck for a configured time window, automatically reduce decoy frequency and increase hint specificity to preserve pacing.
18. **Fail-state UX polish:** On timeout, show a clear “why failed” breakdown (time spent, clues solved, hints used, missed objective) plus a one-click restart that fully resets puzzle/runtime state safely.

---

## Design reference

- **Current build narrative (Week 14):** Grab book (Clue 1) → slide sofa → hidden book (Clue 2) → spin globe (Clue 3) → wall clock → key revealed → exit door; decoys: wrong-couch book (CD sketch), gramophone handle.
