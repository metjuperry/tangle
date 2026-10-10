# Checkpoint D — Your board, saved

2026-10-10. **Stop here: M5 requires approval and a separate per-repo AI opt-in before any provider call.**

## Built

- Tie PRs or stickies by pin drag, or by the card back's **Tie a string** pick mode. New ties are navy; the string panel opens an optional label editor.
- Edit labels, cut strings and re-tie from the **Cut strings** list. Cut rule rows remain cut after sync.
- Create, rename, recolor and archive stickies. Their knots show merged progress of tied PRs, including PRs no longer on the open board. All-merged stickies offer an archive prompt.
- Pin notes on PRs, stickies and strings; your writing uses Caveat.
- Drag cards within their repo zones and stickies anywhere. Saved coordinates win after reload and sync. Zones expand to fit saved coordinates.
- Authenticated authoring endpoints write only to your tables and publish live invalidations. The GitHub cache sync never overwrites those rows.

## Run

```sh
cd web
npm ci
npm run build
cd ..
dotnet run --project src/Tangle.Host
```

Use the same development key and database. No new secrets, schema reset, AI call or GitHub mutation is involved.

## Demo — 10–15 minutes

1. Pick a PR, press **Tie a string**, then choose another PR with the pointer or keyboard. Label it with a real business relationship.
2. Make another tie by dragging from the right half of one pin to the left half of another. Open its navy pencil tag and edit its label.
3. Cut a rule string, sync, and confirm it stays gone. Open it in **Cut strings** and re-tie it.
4. Create a sticky for a real goal. Tie two PRs to it, rename it and choose a color.
5. Add a note to a PR, the sticky and one string.
6. Move a card and the sticky. Reload and sync: ties, cuts, labels, notes and positions should remain.
7. On a branch stack, use **Arrange separately** to move its individual cards. The saved arrangement takes precedence over automatic grouping.
8. Check live updates with two open tabs.

## Verification

Browser tests use only the fixture database and fake GitHub reader. They cover pin-drag tying, keyboard pick mode, labels, notes, cuts/re-ties across sync, a sticky tied to two PRs, card movement and zone-relative persisted coordinates after reload.

API tests exercise all three note targets, labels, cuts, re-ties, stored positions, sticky rename/recolor/archive, merged progress after a tied PR merges, and authentication/reference/input failures. Existing sync tests prove authored ties, cuts, labels, notes, stickies and positions survive changed cache writes.

The working database was not deleted or replaced. No AI provider or GitHub backlog mutation was called.

## Decisions and deviations

- Labels are optional: a tie is saved first, then its label editor opens. Decide whether this feels right or should require a label.
- Initial sticky colors: yellow, blue, pink and green. Decide which you want.
- A batch moves as one card using its representative's saved PR coordinates. Branch stacks have **Arrange separately**; authored individual positions disable automatic stack grouping.
- PR coordinates are relative to repo zones; sticky coordinates are global. This keeps PRs in their repos.
- Cut strings are retained in a separate list, enabling re-tie without deleting history.
- Sticky edits currently send the full title/color/archive state; partial fields are not accepted.
- A sticky's progress counts only `MERGED` as complete; a closed-but-unmerged PR does not fill a knot.

See [DECISIONS](../DECISIONS.md). Remote CI is still not claimed green until GitHub reports a completed run. The local edit in `docs/local-setup.md` remains untouched and excluded.

## Review

Tie three real business links, make one goal sticky, and arrange your cards. Check persistence after sync and browser restart. Approve checkpoint D only after authoring feels reliable; **M5 has not started**.
