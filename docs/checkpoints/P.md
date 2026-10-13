# Checkpoint P — Per-user AI and account management

2026-10-13. **Combined G4/G5 implementation; review before real import/deletion.**

## Built

Settings → AI privacy and budget has Off, My Claude Code, My Anthropic key and
Company key (only visible with an admin grant and `Ai:CompanyEnabled`).
Own keys are encrypted with Data Protection. Switching away removes the stored
own key. Save AI access applies immediately; normal Save settings is separate.

Off makes no provider calls and hides board verdicts. My Claude Code uses the
user's personal MCP key and their own Claude plan; server provider calls stay
off and MCP verdict/link proposals require that mode. Own-key and company modes
remain gated by `Ai:Enabled`, per-repo Allow AI and reserve-before-call budgets.
Company mode is disabled by default; do not enable it until a payer agrees.
Admin-granted budgets cannot be raised by the user.

Account shows numeric identity and a Delete my Tangle action requiring the exact
login. It removes the numeric user database, user-specific backups, tokens,
personal keys and AI key, then clears the session. The Local working database and
Local source backup are never deleted. Other active work blocks deletion; live
event streams are closed after deletion. A filesystem failure keeps the account
disabled and reports failure rather than pretending deletion succeeded.

Administration appears only for `Auth:AdminLogin` (default `metjuperry`) and
offers allowlist editing, user metadata, disable/enable and company funding grants.
There is no access to users' boards. The allowlist persists in the control
database and survives restart; keep the administrator login on it.

## Review

1. Follow checkpoint O only when ready to replace the new GitHub-mode board with
   the backed-up Local copy.
2. In a separate test account, choose My Claude Code and check the displayed MCP
   command with a personal key. Do not commit that key.
3. Confirm Off disables server AI and no key is exposed by API responses.
4. Verify a non-admin cannot access administration.
5. Use Delete my Tangle only on that disposable account, then confirm sign-in,
   personal keys and board access no longer expose its old data. Revoke the
   GitHub App authorization using the linked GitHub settings page.

Provider-backed live testing may cost money and was not performed by the agent.
No real account or working database was deleted. Azure is still deferred.

Local validation passed 120 .NET tests, 50 Local browser tests, three fake-OAuth
browser tests (including AI-mode save and disposable-account deletion), and four
cross-platform hook tests. Provider-backed live testing was not performed.
