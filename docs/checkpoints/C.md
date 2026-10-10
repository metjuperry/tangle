# Checkpoint C — Read-only yarn board

2026-10-10. **Stop here: M4 requires Metju's approval.**

## Built

- React Flow repository group nodes, pinned PR cards, stacked batches and custom sagging yarn edges with button tags. Cards sort by turn and idle; the board pans and zooms.
- Cork, paper, pin colors, stamps, aged paper and a ruled card-back panel follow the prototype. Fonts are self-hosted.
- Counts, others-dimming toggle, Settings and Sync in the top bar. Untangle is visibly unavailable until M6.
- `GET /api/prs/{owner}/{repo}/{number}` reads facts, PR description, unresolved review threads, recent events and strings.
- Authenticated `/api/events` uses .NET 10 ServerSentEvents, increasing IDs, a 256-event replay buffer, `board.reload` for old gaps, and a 30-second heartbeat.
- Events invalidate TanStack Query keys; they do not patch cached data. UI selection and dimming live in Zustand.
- Opening a stale board syncs once after rendering cached data. There are no timers or scheduled syncs. Other clients' syncs refresh the open board and card panel.
- `/debug/prs` and `/api/debug/prs` are removed and return 404.

## Run

```sh
cd web
npm ci
npm run build
cd ..
dotnet run --project src/Tangle.Host
```

Use your existing development key. Open http://127.0.0.1:5202/. Stop the specific old development host before restarting if it still owns that port.

No new keys, schema migrations, AI calls, GitHub mutations or Azure resources are required. Your working database is not replaced or reset.

## Demo — 10 minutes

1. Open the board. Can you immediately identify the orange YOUR TURN cards?
2. Pan and zoom; inspect dashed repo zones, aged paper and stacked batches at your normal screen size.
3. Pick alm-lab #49. Check its four unresolved asks and whose-turn reason in the ruled card back.
4. Click a yarn tag. Read its rule reason and open either linked card.
5. Dim others' PRs. Confirm yours remain full strength.
6. Press Sync with a card open. The board and card back should refetch without resetting your viewport.
7. Open a second board tab and sync there. The first tab should refresh through SSE.
8. Reload after more than ten minutes since the last sync. The stored board appears first; sync then runs once.

## Verification

The browser fixture host uses a fresh SQLite database, recorded #49 review data, 33 recorded scaffold appliers and additional anonymized PRs: 45 PRs across four zones. It substitutes a fake GitHub reader; tests do not call GitHub or AI.

Browser coverage proves zones/cards/batch/yarn, opening four review asks, live refresh following an independent API sync, one stale-open sync and no fresh-open sync, dimming and viewport pan/zoom, Settings persistence and removal of the debug route. API tests cover card context, authentication, event replay, expired/future IDs and heartbeat. Visual capture is generated at 1640×1200, the prototype's reference width.

Final real-data inspection and GitHub CI status are reported with the milestone handoff. Local tests are not described as remote CI.

## Deliberate scope limits and decisions

- This is **read-only**: card dragging, saved positions, tying, cutting, stickies and notes arrive in M4.
- No fabricated AI verdicts or session badges: the dark block shows computed whose-turn facts; the PR description is plain text. AI is M5 and session reports are M7.
- Batch pin/status comes from its highest-priority member. Its card back lists every member. Internal batch strings remain in member context but are not drawn as self-loops.
- Repo labels show short names while card labels show full owner/repo. Confirm whether duplicate short repo names need full labels.
- Semantic zoom, search and the complete keyboard-navigation polish remain M10 per milestone order.
- The initial knot meter stays empty because decisions/Untangle are not built yet.
- The remote's README-only initial history is preserved through merge commits. No force push or PR merge is performed.
- The uncommitted local setup edit is excluded from publication because it may contain a development key.

See [DECISIONS](../DECISIONS.md) for reversible choices.

## What Metju decides

Use the board on your real backlog for ten minutes. Can you tell what needs you within seconds? Are cards, zones, yarn and card-back text readable at your usual zoom? Decide visual changes against the prototype, including zone packing, card size, the default fit and batch representation.

Approve checkpoint C before any editing features are added. **M4 has not started.**
