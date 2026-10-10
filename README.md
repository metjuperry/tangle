# Tangle

A personal yarn board for triaging GitHub pull requests across repositories.
Pinned cards show whose turn it is; strings connect related work. Notes, ties,
cuts and layouts survive sync. AI only proposes; GitHub actions require an
explicit confirmation.

## Run locally

Requires .NET 10, Node.js 24 and GitHub CLI.

```sh
cd web
npm ci
npm run build
cd ..
dotnet user-secrets set "Tangle:DevelopmentKey" "<random key of at least 32 characters>" --project src/Tangle.Host
dotnet run --project src/Tangle.Host
```

Open http://127.0.0.1:5202 and unlock with the development key. Configure watched
owners/repos in Settings, then Sync. GitHub credentials come from `gh auth token`
or Host user-secrets, never the database or browser.

## How it works

Open **Help** in the toolbar for the feature explanation panel, or read the
[user guide](docs/user-guide.md). Both use the same maintained feature text.

Board and Untangle are the daily views. Settings controls scope, provider
permissions and keys; evaluation/call history lives under Settings. Receipt
history has its own toolbar button.

## Board controls

Pan and zoom; cards become compact below 60% and repo summaries below 35%.
**Ctrl+K / Cmd+K** searches PR numbers and titles. Tab focuses cards zone by zone;
arrow keys move to the nearest card. Pins and card backs offer keyboard tying.
Untangle supports M/F/P/W/X/S and U undo, with reduced-motion support.

The board reads SQLite immediately and refreshes through live events. Switching
Board/Untangle keeps the app open and shows a yarn loading overlay when needed.
Lock Tangle in Settings ends the browser session and clears its in-memory data.
Offline authentication is not bypassed.

## Safety and setup

- [Local settings, tokens and tests](docs/local-setup.md)
- [Claude Code MCP and session hook](docs/setup-claude-code.md)
- [Evaluation and Jev setup](docs/checkpoints/H.md)
- [Deployment deferred](docs/deployment-status.md)
- [Implementation checkpoints](docs/03-implementation-order.md)

Azure deployment and a standalone demo dataset are deferred by choice. Normal
tests still use isolated anonymized fixtures; no production database is reset.

AI opt-in is per repo. Keys stay in environment variables or user-secrets.
Apply defaults off and is restricted to the configured sandbox until explicitly
approved for real repositories. There is no MCP Apply tool.

## Validation

```sh
dotnet test
node --test tests/hooks/session-hook.test.mjs
cd web
npm run lint
npx playwright install chromium
npx playwright test
```

Tests use temporary fixture databases and fake provider handlers, not the
working database or paid AI calls.

Feature documentation is shared with in-app Help. After editing
`docs/features.json`, run `node docs/generate-guide.mjs`; CI checks it with
`node docs/generate-guide.mjs --check`.
