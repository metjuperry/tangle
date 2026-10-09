# Checkpoint A — GitHub data completeness

2026-10-10. **Stop here: M2 requires Metju's approval.**

## Built

- M0: .NET 10 solution and inward project references; local loopback host, development-key authentication, Host/Origin checks and `/healthz`; SQLite numbered forward migrations; embedded Vite/React shell with self-hosted fonts; .NET and Playwright tests; GitHub Actions pipeline.
- M1: paged read-only GraphQL client, organization/personal/repo scopes, settings persisted in SQLite, content hashes, atomic cache writes per scope, incremental open/closed searches, nested pagination, plain Settings and temporary `/debug/prs`, open-only `/api/board`.
- User-authored tables remain untouched by sync. Every repo's AI switch stays off.

## Run

Follow [local setup](../local-setup.md): build `web/`, set `Tangle:DevelopmentKey` in Host user-secrets, and run `dotnet run --project src/Tangle.Host`. Open http://127.0.0.1:5202 with that key.

GitHub CLI already authenticates as `metjuperry` on this machine. Alternatively set `GitHub:Tokens:<owner>` in user-secrets or `GitHub__Tokens__<owner>` in the environment. Never enter GitHub tokens into the browser.

Do not copy the validation database over your working database. Validation used a separate file in the Copilot session workspace; normal startup uses `~/.tangle/tangle.db` and only applies forward migrations.

## Demo — 10 minutes

1. Open Settings. Add TALXIS (Organization) and metjuperry (Personal account), save, and press Sync.
2. Open `/debug/prs`. Compare the records with GitHub, including expected private repos and review-requested PRs.
3. Open Board. Confirm cached closed/merged PRs do not appear.
4. Sync again after about a minute. If nothing changed, expect zero changed PRs and only a few search points.
5. Disable a repo, save, and verify it disappears from Board; enable it again. Check the defaults you want before approving M2.

## Evidence

The isolated real sync stored **17 open PRs across six repositories** for the authenticated account; all 17 were authored by that account. The public recorded fixture retains #49's four unresolved review threads. No completeness claim is made for private access or review requests until Metju checks them.

The initial full sync cost **106 search points**. The original nested incremental query unnecessarily cost 212 points even for empty results; after adding a lightweight changed-PR probe, the same unchanged two-owner sync used **four calls / four points / zero changes**.

Local .NET tests cover migrations, authentication, scope validation, paging, incremental windows, closed-PR removal, unchanged sync, failed-page rollback, nested pagination, recorded mapping, and preservation of authored rows. Playwright covers unlocking the embedded shell and saving/reloading Settings.

## Deviations and outstanding delivery blockers

See [DECISIONS](../DECISIONS.md). Reversible additions: owner kind, reviewer cache and AI-call schema tables, cookie-based browser key exchange, cheap incremental probe, synchronous M1 sync response. No facts, board graph, AI or GitHub writes have been implemented.

This folder originally had **no Git repository or remote**. Work is committed on local `milestone/m0` and stacked `milestone/m1` branches. **Milestone PRs and green remote CI are not yet available.** Provide the intended repository destination and authorization to publish the milestone branches; do not merge either without explicit approval.

## Decisions for Metju

- Which owners and repos should be watched by default?
- Keep the design's `involves:<viewer>` scope (authored, review-requested, and other involvement), or narrow it? The current real run only returned authored PRs; validate whether expected review-requested/private PRs are missing because of scope or access.
- Confirm owner/repo precedence: disabling an owner hides and pauses all its discovered repos; repo toggles narrow an enabled owner; standalone repo scopes work when its owner is absent from the owner list.
- Approve the reversible deviations in DECISIONS and supply the GitHub repository destination for the two milestone PRs.
- Approve checkpoint A only after checking completeness; **M2 will not start before approval.**
