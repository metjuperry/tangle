# Checkpoint L — Per-user databases behind the local key

2026-10-12. **Local-mode review accepted:** Metju reported it works just like
it did yesterday. G2 / real GitHub sign-in has not started.

## Built

- A forward-versioned control database for users, encrypted-token storage
  columns, AI-mode storage columns, allowlist and user-owned hashed personal keys.
  Token encryption and OAuth population belong to later milestones; no real
  tokens have been stored.
- Request-scoped database and data services. The local identity uses the
  existing configured SQLite path without renaming, replacing or deleting it.
- Numeric user database files are migrated and recomputed on first open.
  Inactive database wrappers are evicted after 30 minutes; request contexts keep
  their own resolved wrapper. Connections remain short-lived.
- Independent event histories, sync locks and replay locks for each identity.
  Background AI jobs carry a user identity and open a fresh scope.
- Legacy personal-key hashes are copied idempotently into the control database.
  Original rows are retained to preserve rollback data; revocation thereafter
  uses the control database, never the old rows.
- Nonlocal users cannot fall back to `gh` or server GitHub credentials.
  Provider-backed AI for nonlocal users remains off until per-user credentials
  and policies are implemented. Existing local AI settings retain their behavior.

## Prerequisites

PR #29 implements #11–#14 and #16: cursor-reset closure reconciliation, focused
verdict shortcuts, claimed-action error recovery, stable decision timestamps,
and configured host/origin plus trusted-ingress principal handling. It remains
unmerged; G1 is based on it and must be merged after it, not independently.

Container principal mode resolves only pre-provisioned users in the control
database. It never silently opens the local user's board. OAuth provisioning is
G2 work. Use Local mode for this checkpoint.

## Run and review — 10 minutes, then normal-day use

1. Build `web/` and restart the host in `Auth:Mode=Local` (the default), with
   your existing development key and database path.
2. Confirm the same cards, strings, cuts, notes, stickies and saved positions.
3. Run Sync and check a cached PR closed during a cursor reset leaves the board.
4. Generate/revoke a test agent personal key, then try the session hook/MCP.
   Existing active keys should continue to work.
5. Use Untangle by clicking a verdict and then using its keyboard shortcuts.

The new control file is `data/control.db` beside the configured database;
other user files live under `data/users/<numeric-id>.db` in that directory.
No real user database was opened or modified during automated verification.
Back up your existing SQLite file as usual before reviewing.

## Verification and boundaries

Automated tests use isolated temporary databases and fake provider handlers.
They cover two identities with the same PR identifier but separate context,
notes, actions, keys, sessions, events and MCP output; disabled users are denied.
Legacy-key copying is idempotent and preserves source rows.

GitHub OAuth, token refresh, account UI, importing to the numeric GitHub
identity, per-user AI selection, deletion and admin UI are **not implemented at
L**. They belong to G2–G5 and checkpoints M–P. GitHub App readiness must be
confirmed before the first real sign-in at M. No Azure resources, paid calls or
real GitHub backlog mutations were performed.

## Decision at L

Confirm your normal local board and agent integrations behave unchanged.
Approval advances to G2, which replaces the key form only in GitHub mode.
