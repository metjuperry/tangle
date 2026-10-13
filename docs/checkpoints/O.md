# Checkpoint O — Import the Local board

2026-10-13. G4 and G5 were combined at Metju's explicit request; no intermediate
stop was required. **The import command is implemented, but has not been run on
Metju's working data.**

## Import

Sign in once in GitHub mode and note your numeric GitHub user ID in Settings →
Account. Stop the host, then run:

```sh
dotnet run --project src/Tangle.Host -- --import-local --target-user <numeric-id> --confirm-login metjuperry
```

The configured `Tangle:DatabasePath` is the source. The command verifies the
destination login and source viewer, snapshots the source using SQLite's backup
API, backs up an existing destination, migrates only the copy, and replaces
`data/users/<id>.db`. The original Local database remains unchanged. A host-file
lock prevents running this command while the updated host is serving requests.
Stop older host versions too; they do not participate in that lock.

Every PR, string, cut, note, sticky, position, action, decision, receipt, session,
AI call and evaluation row is copied. Personal-key hashes already in the control
database are reassigned from Local to the GitHub user; the old user-database key
rows are not imported. Sandbox Apply permission resets to off and AI access is
not opted in by import.

Restart and compare your board, saved receipt history and notes. Existing
GitHub-mode data is replaced, not merged, but its backup remains in
`data/backups/`. Inspect before running and keep backups private.

Deletion of a GitHub account does not delete the original Local database or
its source backup. Those are deliberately retained outside that account.

## Verification

Isolated tests cover source preservation, backup creation, authored-row copying,
credential exclusion and wrong-login refusal. No actual user import occurred.
