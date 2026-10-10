# Checkpoint F — Untangle, receipt and confirmed Apply

2026-10-10. **Stop here: live sandbox Apply remains blocked, and M7 requires approval.**

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

For your own manual sandbox run:

```sh
dotnet user-secrets set "Apply:SandboxRepo" "metjuperry/tangle-sandbox" --project src/Tangle.Host
dotnet user-secrets set "Apply:Enabled" "true" --project src/Tangle.Host
```

Restart the host. Your GitHub credential needs the relevant write permissions for that sandbox. Keep it restricted; no additional token is stored in SQLite. **Do not point this setting at real repos before the checkpoint F safety decision.**

## Verification and gaps

Offline API tests prove explicit confirmation, disabled Apply, exactly-once claiming, undo removing queued actions, and GitHub ownership/head-SHA guards. Browser tests cover a full deck with undo, exact receipt text, cancellation versus confirmation, and reduced-motion rendering. Fake HTTP handlers substitute for every GitHub mutation; no paid AI calls are required.

**Live sandbox end-to-end testing has not occurred** because no sandbox/action authorization has been supplied. Remote CI is not claimed green until GitHub registers and completes a run.

## Decisions/deviations

- Fix, Ping and Split intent cannot safely become GitHub mutations automatically; the exact action editor makes the actual operation explicit. No generated comment is posted without review.
- Wait/Snooze persist local decisions but do not enqueue GitHub actions. Snooze leaves the current deck until undo; a time-based snooze/revisit rule is unspecified and not invented.
- Close posts no comment; queue a separate comment to explain it. Closing another person's PR is blocked.
- Merge initially uses MERGE strategy and requires entering the expected SHA. Decide whether merge should stay disabled and whether to offer other methods later.
- Undo cannot reverse an already attempted GitHub action. It only removes local decisions/queued receipt entries.

Review your real deck but apply only in the sandbox. Decide allowed repositories and action kinds after inspecting the confirmations and real sandbox results. **M7 has not started.**
