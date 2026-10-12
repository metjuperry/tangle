# Checkpoint M — GitHub sign-in

2026-10-12. **Accepted:** Metju successfully signed in and requested G3.

## Built

GitHub mode adds browser sign-in with random one-use state, S256 PKCE, a
ten-minute encrypted HttpOnly flow cookie and numeric GitHub identity sessions.
Allowed logins or verified active organization memberships admit users; denied,
expired and replayed flows show friendly errors. Disabled users and removed
allowlist entries cannot keep using sessions. Local mode remains the default.

GitHub tokens are encrypted with ASP.NET Core Data Protection in the control
database, never returned to browsers. Keys persist outside both databases under
the sibling `keys/` directory, or `Auth:KeyDirectory`. Keep this folder private
and backed up; local key files are not encrypted by a platform key vault.

Settings has an Account section with identity, token status, sign out,
installation listing and install link. Missing watched-owner installations are
reported. GitHub-mode Sync is intentionally disabled until G3; existing local
data remains separate until the G4 copy/import. No production SQLite file was
opened or moved during implementation.

## Configure for your first real sign-in

Put these in **Tangle.Host user-secrets** or the equivalent environment
variables; do not paste the secret into chat:

```sh
dotnet user-secrets set "GitHub:App:ClientId" "<client ID>" --project src/Tangle.Host
dotnet user-secrets set "GitHub:App:ClientSecret" "<client secret>" --project src/Tangle.Host
dotnet user-secrets set "GitHub:App:CallbackUrl" "http://127.0.0.1:5202/auth/github/callback" --project src/Tangle.Host
dotnet user-secrets set "Auth:AllowedLogins:0" "metjuperry" --project src/Tangle.Host
dotnet user-secrets set "Auth:Mode" "GitHub" --project src/Tangle.Host
```

Before doing this, confirm **that exact callback** is registered on
`tangle-metjuperry`, user-token expiry is enabled, and the app is installed on
your account. For organization admission, configure `Auth:AllowedOrganizations`
and grant Members read. Any changed host/port requires the exact corresponding
callback registered by you; the agent has not changed GitHub App settings.

## Review — 10 minutes

1. Build `web/`, restart the host, and click **Sign in with GitHub**.
2. Confirm your first sign-in opens Settings → Account with your login and
   active token status. Your numeric-user board is initially empty; this is not
   a lost local board.
3. Inspect app installations and the install link. Do not expect real Sync
   yet; G3 routes and refreshes the user token.
4. Sign out; retry a previous callback URL and confirm it is refused.
5. Try an account not on the allowlist; it should get a friendly refusal.
6. Return to `Auth:Mode=Local` and restart to see your existing board again.

## Validation and limitations

Fake server tests cover PKCE, encrypted tokens, numeric identity across renames,
separate user boards, organization admission, disabled/removed access, token
exchange errors and login rate limits. A separate Playwright fake-OAuth suite
tests the GitHub button, Account, sign out, refusal and callback replay.
External GitHub navigation is blocked in that suite.

Local validation passed 113 .NET tests, 49 local-mode browser tests and two
fake-OAuth browser tests. CI runs both browser configurations.

No real authorization or installation was attempted. Refresh/revocation routing
is G3, local-data import G4, AI modes/deletion/admin G5. Allowlist settings are
server configuration at M; admin editing follows in G5. Organization membership
is verified at sign-in and its admission is bounded by the eight-hour session.

Approve M after your first real sign-in and refusal check to continue to G3.
