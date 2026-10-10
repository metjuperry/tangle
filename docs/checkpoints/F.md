# Checkpoint F — Untangle, receipt and confirmed Apply

2026-10-10. **Stop here: Metju has verified sandbox comment, close and merge. M7 still requires checkpoint F approval.**

## Built

- Untangle orders your open PRs by whose turn, then nobody's turn by idle days, then the rest by age. AI picks are highlighted.
- M/F/P/W/X/S choices and U undo, with Motion exits and reduced-motion fades. Decisions persist separately from queued actions.
- Merge/Fix/Ping/Close open an exact-action composer; queue a merge, comment, review request or close, or explicitly record only the local decision. Wait/Snooze record local decisions without posting.
- Queued actions appear on the till-roll receipt, can be removed, and show their exact payload.
- Apply requires a separate confirmation for each action. The server checks a sandbox-only allowlist before contacting GitHub.
- GitHub adapter inspects current PR state before mutation, pins merge to the expected head SHA, forbids close/merge on someone else's PR, and never retries automatically.
- Actions are claimed atomically so double Apply cannot post twice. Failed or uncertain attempts are recorded and require manual GitHub inspection, not silent retry.
- Settings shows the server's Apply safety policy. No browser setting can expand its sandbox allowlist.

## Run

Build `web/` and restart the host as usual. Open **Untangle** in the top bar. By default Apply is disabled; decisions and queuing work on real data without posting anything.

Forward migration 0008 adds persisted decisions. It does not delete or replace your working database.

## Demo — 10–15 minutes

1. Open Untangle, check its order and read the suggested pick.
2. Press F, type a draft comment and queue it. Confirm its exact text appears on the receipt; nothing has been sent.
3. Press U to undo the linked decision and remove that action from the receipt.
4. Try Wait/Snooze and a Merge/Close choice recorded locally without an action. Complete the deck.
5. Queue a comment from a card back, then open Untangle's receipt and remove it.
6. With a configured sandbox only, click Apply. Check the target, kind, exact text/reviewer/SHA in the confirmation. Cancel first; it must send nothing.
7. Confirm one sandbox action, inspect GitHub, then try Apply again: an attempted/applied action cannot be sent twice.

## Sandbox prerequisite — Metju must provide

Create a throwaway repo, such as `metjuperry/tangle-sandbox`, with disposable PRs, and explicitly authorize the action kinds to test there. The agent has not created a repo or posted any GitHub mutation.

Metju created `metjuperry/tangle-sandbox` and explicitly authorized throwaway test-data creation. A baseline and three independent PRs are now available: [comment #1](https://github.com/metjuperry/tangle-sandbox/pull/1), [close #2](https://github.com/metjuperry/tangle-sandbox/pull/2), and [merge #3](https://github.com/metjuperry/tangle-sandbox/pull/3). All remain open; no test comment, review request, close or merge was performed. Live Apply still needs separate authorization for those operations.

For your own manual sandbox run:

```sh
dotnet user-secrets set "Apply:SandboxRepo" "metjuperry/tangle-sandbox" --project src/Tangle.Host
dotnet user-secrets set "Apply:Enabled" "true" --project src/Tangle.Host
```

Restart the host. Your GitHub credential needs the relevant write permissions for that sandbox. Keep it restricted; no additional token is stored in SQLite. **Do not point this setting at real repos before the checkpoint F safety decision.**

## Verification and gaps

Metju manually confirmed comment #1: cancelling sent nothing, and confirming created the comment successfully. The sandbox restriction was visible. Applied results now use concise labels (Comment posted, Review requested, PR closed/merged) and a View on GitHub link rather than raw mutation JSON; failed/uncertain results keep their explicit error. Raw responses remain stored for diagnosis.

Metju subsequently confirmed the sandbox close and merge succeeded and Sync removed both PRs from the board. Post-sync progress now counts only distinct decisions for current own open PRs, avoiding ratios such as 3 of 1. Applied actions remain saved in a collapsed Completed actions receipt history; failed/in-flight attempts remain visible. The active receipt total counts queued actions only.

**Tear and save** closes a triage round: applied actions and all current decisions are atomically assigned to a saved receipt, without deleting them. Current beads reset and every still-open PR returns to Untangle. Pending queued actions and failed attempts remain visible; an in-flight Apply blocks tearing. Saved receipts are expandable history with action results and decisions. Notes, strings, positions, GitHub cache and AI-look state are untouched, so starting a fresh round does not cause paid AI rechecks.

Published as [PR #7](https://github.com/metjuperry/tangle/pull/7), based on M5 and left unmerged. Offline validation passed 83 .NET tests and 22 browser tests. GitHub reports no workflow runs for this branch, so remote CI remains unverified.

Offline API tests prove explicit confirmation, disabled Apply, exactly-once claiming, undo removing queued actions, and GitHub ownership/head-SHA guards. Browser tests cover a full deck with undo, exact receipt text, cancellation versus confirmation, and reduced-motion rendering. Fake HTTP handlers substitute for every GitHub mutation; no paid AI calls are required.

**Metju completed live sandbox comment, close and merge tests.** The implementing agent performed no Apply mutations. Request-review testing has not been authorized or reported. Remote CI is not claimed green until GitHub registers and completes a run.

## Decisions/deviations

Fix offers **Still my turn**, which acknowledges the review work and moves on without changing computed facts or creating a GitHub action. There is no to-do list. **Draft a comment instead** is a separate option. Like Wait/Snooze, the local decision persists and Undo brings its card back.

Board/Untangle navigation now stays in the app. A paper-and-yarn loading overlay keeps the previous view visible while the lazy deck and required data load, with a static reduced-motion version and explicit errors. Cached switches have no artificial delay; browser back/forward is supported.

- Fix can record a local “Still my turn” decision; Ping and any explicitly drafted comment still require the exact action editor. No generated comment is posted without review.
- Wait/Snooze persist local decisions but do not enqueue GitHub actions. Snooze leaves the current deck until undo; a time-based snooze/revisit rule is unspecified and not invented.
- Close posts no comment; queue a separate comment to explain it. Closing another person's PR is blocked.
- Merge uses MERGE strategy and automatically captures the current head from a read-only GitHub request when queued; nobody types a SHA. Apply rechecks and pins that version so new commits block the merge. The confirmation shows version details only on demand. Decide whether merge should stay disabled and whether to offer other methods later.
- Undo cannot reverse an already attempted GitHub action. It only removes local decisions/queued receipt entries.

Review your real deck but apply only in the sandbox. Decide allowed repositories and action kinds after inspecting the confirmations and real sandbox results. **M7 has not started.**
