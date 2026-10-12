# Checkpoint N — The user's GitHub token everywhere

2026-10-12. **G3 implemented. Stop before G4 import.**

## Built

GitHub-mode sync, PR inspection, reviewer lookup, Apply and installation listing
use the caller's encrypted GitHub App user token. Local mode retains its existing
credentials; a nonlocal board never falls back to them.

Tokens within five minutes of expiry refresh under a per-user lock. Failed or
uncertain refresh and GitHub HTTP 401 invalidate access without a retry loop.
The cached board remains readable with a sign-in-again banner; Sync and Apply
are disabled until access is restored. HTTP 403/404 identify installation or
permission problems rather than silently using another credential.

GitHub users separately opt into the server-configured sandbox under Settings →
GitHub Apply safety. Permission changes apply immediately. The server's
Apply enablement, sandbox restriction, head-version check, own-PR restrictions,
individual confirmation and proposal-only MCP remain unchanged. Real-repository
Apply is still not enabled.

## Review — 10 minutes

1. Build `web/` and restart in GitHub mode, then sign in.
2. Add your personal owner or installed repositories in Settings and Save.
3. Hit Sync. Confirm only repositories available to your GitHub App installation
   appear. A missing TALXIS installation needs an organization owner's action.
4. Compare facts and PR states with the Local-mode board; user-authored notes
   and strings are not imported yet.
5. Inspect Account/token status and the sandbox opt-in setting. Do not apply
   real backlog actions. Sandbox Apply still requires your explicit confirmation.

## Validation and boundaries

Tests use encrypted fake tokens and fake OAuth responses. They cover concurrent
refresh (one refresh per user), independent credentials, failed refresh,
invalid-token suppression, HTTP 401 invalidation and separate sandbox opt-ins.
Existing local suites remain required. No real sync, refresh, paid AI or GitHub
mutation was initiated during implementation.

Local validation passed 117 .NET tests, 50 local-mode browser tests and two
fake-OAuth browser tests. Test hosts explicitly force Local mode unless running
the OAuth suite, so developer user-secrets cannot change test authentication.

Your successful login accepted checkpoint M. G4 copies your existing local
database only after N approval, with a backup and without rewriting the source.
Per-user AI modes and account deletion/admin remain G5.
