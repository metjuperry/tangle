# Tangle: GitHub sign-in and per-user data

Oct 11, 2026 · @Matej Samler

## Goal and scope

Replace the development key with **Sign in with GitHub**, and give every person who signs in their own Tangle: their own database, synced with their own GitHub token. Nobody ever sees a PR, string, note or AI verdict that belongs to someone else's board.

**In scope**

- GitHub sign-in for the board, with an allowlist of who may sign in.
- One SQLite database per user, plus a small control database for users, tokens and personal keys.
- Sync, Apply and every GitHub lookup use the signed-in user's token.
- MCP, the session hook, live events and the AI worker all resolve to one user.
- A per-user choice of how AI runs: off, through their own Claude Code, their own Anthropic key, or a company key with a budget.
- Bringing Metju's current database across as his first user database.

**Not in scope**

- Shared stickies, shared strings or a team view. Each board stays private; sharing can come later as an explicit copy between databases.
- Hosting itself (M9: container, Litestream, Terraform). This plan works on `localhost` first and only needs #16 (configurable address and sign-in modes) to run anywhere else.
- Azure DevOps sign-in. That arrives with the Azure DevOps sync feature.

## Decisions before work starts

The agent stops at G0 until Metju has answered these.

| Decision | Options | Recommendation |
| --- | --- | --- |
| Kind of GitHub app | **GitHub App** with user tokens: short-lived (8 hours) with a refresh token, fine-grained permissions, but it only sees repos where the app is installed, so an owner of TALXIS (and of every other org) must install it. **OAuth App** with the `repo` scope: sees everything the user can, no install step, but the token is broad and long-lived, and orgs that restrict third-party OAuth apps must approve it. | Decided: GitHub App, registered as tangle-metjuperry on 12 Oct 2026. |
| Who may sign in | A list of GitHub logins; members of named orgs; anyone | A list of logins plus members of named orgs. "Anyone" only when Tangle is public. |
| Local development | Keep the development key as a single local user; or require GitHub sign-in even on localhost | Keep it: `Auth:Mode=Local` maps the key to one fixed user, so tests and offline work stay simple. |
| Default AI mode for new users | Off; own Claude Code; own key; company key | Off. A user opts in on their Settings page. |
| Who pays for a company key | Nobody (mode hidden); Metju; NETWORG | Hidden until someone agrees to pay; budget per user when it appears. |

## Registering the GitHub App

**Done by Metju on 12 Oct 2026.** The app exists; the tables below record how it is meant to be configured. The agent never changes the app's settings on GitHub and never sees its client secret. If the app needs a change (a callback URL, a permission), the agent asks Metju.

| Field | Value for Tangle | Why |
| --- | --- | --- |
| GitHub App name | `Tangle (metjuperry)` or similar, at most 34 characters, unique across GitHub | Shown on the consent screen and in org install lists |
| Homepage URL | `https://github.com/metjuperry/tangle` | No website needed |
| Callback URL | `http://127.0.0.1:<port>/auth/github/callback`; add the hosted URL later (up to 10 callback URLs) | Must match the `redirect_uri` the app sends |
| Expire user authorization tokens | On (the default) | 8-hour access tokens plus 6-month refresh tokens, as the Tokens section assumes |
| Request user authorization (OAuth) during installation | On | Whoever installs it is signed in straight away; this disables the setup URL, which Tangle doesn't need |
| Enable Device Flow | Off | Only the browser flow is used |
| Webhook → Active | Off | Tangle does no background work; sync runs when you open the board |
| Where can this GitHub App be installed? | Any account | So TALXIS and colleagues' accounts can install it |

**Permissions** (the minimum; each user's token gets the overlap of these and what that person can do):

| Permission | Access | Used for |
| --- | --- | --- |
| Repository → Metadata | Read | Required by GitHub for every app |
| Repository → Pull requests | Read and write | Sync PRs, reviews and threads; comment, request review, close |
| Repository → Contents | Read and write | Commits and changed files in sync; merge from Apply |
| Repository → Checks | Read | CI state for the whose-turn rules |
| Repository → Commit statuses | Read | CI state from older status checks |
| Organization → Members | Read | Only if the sign-in allowlist uses org membership |

If Apply stays sandbox-only for now, Pull requests and Contents can start as read-only and be raised later; GitHub then asks each installation to accept the new permissions.

**After registering:**

1. On the app's page, note the **Client ID** (not the App ID) and generate a **client secret**. Both go into user-secrets (`GitHub:App:ClientId`, `GitHub:App:ClientSecret`), never into the repo. No private key is needed: Tangle only uses user tokens, not installation tokens.
2. **Install the app** on your personal account and choose the repos (or all). Then ask a TALXIS owner to install it on TALXIS. Without an installation on an org, nobody sees that org's PRs in Tangle, even if they are members.
3. Every user must have a verified primary email on GitHub, or GitHub refuses to issue their token.

### Using the registered app (for the implementing agent)

| Item | Value |
| --- | --- |
| App | **Tangle (metjuperry)**, owned by metjuperry |
| Slug | `tangle-metjuperry` |
| Settings (Metju only) | https://github.com/settings/apps/tangle-metjuperry |
| Install link shown in Tangle | `https://github.com/apps/tangle-metjuperry/installations/new` |
| Client ID | `GitHub:App:ClientId` in Tangle.Host user-secrets (or env `GitHub__App__ClientId`) |
| Client secret | `GitHub:App:ClientSecret` in user-secrets (or env `GitHub__App__ClientSecret`); never logged, committed or echoed |
| Callback | `GitHub:App:CallbackUrl`, default `http://127.0.0.1:<Tangle port>/auth/github/callback`; it must match a callback URL registered on the app exactly |
| Sign-in mode | `Auth:Mode=GitHub`; `Auth:Mode=Local` stays the default until checkpoint M |
| Endpoints | Authorize `https://github.com/login/oauth/authorize` (client\_id, redirect\_uri, state, code\_challenge with S256); token and refresh `POST https://github.com/login/oauth/access_token` (`grant_type=refresh_token` for refresh); identity `GET https://api.github.com/user`; installations `GET https://api.github.com/user/installations` |

**Rules for the agent.**

- Tests never call GitHub: they use the fake OAuth server described under Testing, with fake client ids.
- The first real sign-in is Metju's, at checkpoint M. Before that, the agent may only check that the configured values are present ("client id set", "secret set"), never print them.
- If the configured port or host differs from a registered callback URL, stop and ask Metju to add it on GitHub; don't change the redirect to work around it.
- A 403 or 404 from GitHub with a user token means the app lacks a permission or isn't installed on that org. Report which one in the UI and in the checkpoint note; never fall back to another token.
- Settings lists the user's installations and links to the install page above when a watched owner has none.

**Still open on 12 Oct 2026.**

- [ ] The app's public page still says it is a *private* GitHub App, so only metjuperry can install it. To let TALXIS install it: app settings → Advanced → **Make public** (installable by others, not listed anywhere).
- [ ] Installed on metjuperry.
- [ ] Installed on TALXIS by an org owner.
- [ ] Client ID and secret in user-secrets on Metju's machine.
- [ ] Metju confirms the callback URL, token expiry and permissions match the tables above.

**What the code does with it.** Sign-in uses GitHub's web flow with `state` and PKCE (`S256`). After sign-in, Settings lists the user's installations (`GET /user/installations`) and warns when a watched org has no installation, so a missing install shows up as a clear message, not an empty board.

## How it works

1. The browser opens Tangle without a session and sees the cork login screen with one button, **Sign in with GitHub**.
2. `GET /auth/github/login` stores a random `state` (and a PKCE verifier, S256) in a short-lived, HttpOnly cookie and redirects to GitHub's authorize page.
3. GitHub redirects to `GET /auth/github/callback`. The server checks `state`, exchanges the code for a token, and reads the user (`GET /user`: numeric id, login, name, avatar) and, if the allowlist uses orgs, their memberships.
4. Not allowed → a friendly "not on the list" page; nothing is stored. Allowed → the control database upserts the user by **numeric GitHub id** (logins can be renamed), the token is encrypted and stored, and the browser gets the session cookie (`tangle.session`: HttpOnly, Secure when not on localhost, SameSite=Lax so the GitHub redirect can set it).
5. Every later request goes through a **user resolver**: session cookie → user id; personal key → user id; local mode → the fixed local user. It loads that user's context (database, token, AI mode) for the request.
6. The first time a user signs in, their database is created and migrated, and the board opens empty on Settings so they can choose what to watch. Their first sync then runs with their own token.
7. **Sign out** clears the session. **Delete my Tangle** in Settings removes their database file, tokens, personal keys and AI key, and tells them how to revoke the app on GitHub.

## Tokens

- **Where they live.** In the control database, table `user_token`, encrypted with ASP.NET Core Data Protection. Never in the user's own database, never in logs, never sent to the browser. Data Protection keys are stored outside both databases (a key folder locally; Blob Storage wrapped by Key Vault when hosted, as the phase 3 note in the Technical design already says).
- **Use.** `GitHubTokens` stops reading `gh auth token`, environment variables and per-org secrets in GitHub mode. It returns the signed-in user's token, so one user's token can never sync another user's board. Local mode keeps today's behaviour.
- **Refresh.** GitHub App user tokens expire after 8 hours and their refresh tokens after 6 months; an expired refresh token sends the user back to sign-in. Before each GitHub call, a token within 5 minutes of expiry is refreshed with the stored refresh token, under a per-user lock so two requests don't refresh at once.
- **Revocation.** A 401 from GitHub, or a failed refresh, marks the token invalid. The board keeps showing stored data with a banner, "GitHub access expired, sign in again", and sync and Apply are disabled until the user signs in again. Nothing retries in a loop.
- **Scopes and permissions.** GitHub App: Pull requests read and write (comments, review requests, close), Contents read and write (merge), Metadata read, Members read only if the allowlist uses org membership. OAuth App: `repo` and `read:org`. Apply keeps every safety rule it has today.

## Per-user data

**Two kinds of database.**

| Database | File | Holds |
| --- | --- | --- |
| Control | `data/control.db` | `user` (github\_id, login, name, avatar\_url, created\_at, last\_seen\_at, disabled), `user_token` (user\_id, encrypted access and refresh token, expires\_at, status), `personal_key` (hash, user\_id, label, created\_at, revoked\_at; moved here from today's schema), `user_ai` (user\_id, mode, encrypted Anthropic key, monthly budget), `allowlist` (login or org) |
| User | `data/users/<github_id>.db` | Today's schema, unchanged: PRs, facts, strings, stickies, notes, positions, actions, receipt rounds, sessions, AI calls, evaluation |

Keeping today's schema inside each user database means almost no query changes. Personal keys move to the control database because a key must find its user before any user database is opened.

**Code changes.** Today `Program.cs` registers `Database` and every store (`SyncStore`, `AuthoringStore`, `ActionStore`, `AiStore`, `EvaluationStore`, `SessionService`…) as singletons over one file.

- Add `IUserContext` (user id, login, database, token provider), resolved once per request by the user resolver.
- Add `UserDatabases`: opens `data/users/<id>.db`, migrates on first open, keeps a small cache of open handles and evicts idle ones.
- Stores take their `Database` from `IUserContext` (scoped) instead of a singleton. Where a background service needs a store, it asks `UserDatabases` for that user explicitly. No store keeps a static or cached reference to a database.
- Startup no longer recomputes one database; each user database recomputes when it is first opened after a deploy.
- One `Database` class still runs migrations, so the forward-only rule and its tests apply to every user file.

## What changes, part by part

| Part | Change |
| --- | --- |
| Sync | Runs for the signed-in user with their token. The "one sync at a time" lock becomes per user. `involves:` uses their login, which the app already reads. |
| Apply | Uses the user's token, so GitHub shows them as the author of every comment, close and merge. The repo allowlist (#15) becomes a per-user setting. |
| AI worker | One queue entry per user (`Enqueue(userId)`); the worker opens that user's database, uses that user's AI mode and budget, and never mixes users in one request. |
| Live events | `EventHub` becomes one hub per user; `/api/events` subscribes to the caller's hub only. Events from one user's sync, MCP call or hook never reach another browser. |
| MCP and session hook | A personal key resolves to its user through the control database; tools read and write that user's database. Nothing changes in `claude mcp add` or the hook script except the URL. |
| Settings | New **Account** section: GitHub identity, token status, sign out, delete my Tangle. Personal keys stay, now per user. The **AI** section gains the mode choice. |
| Login screen | The cork login keeps its look; the key field becomes the GitHub button in GitHub mode and stays as is in local mode. |
| Admin | A small page for Metju only: allowlist, list of users with last-seen date, disable a user. No access to anyone's board. |

## AI access per user

GitHub Models has been retired, so AI access comes from one of four modes, chosen per user in Settings. The board works fully with AI off: facts, whose turn, batches and rule strings never needed it.

| Mode | How it works | Who pays |
| --- | --- | --- |
| Off (default) | No AI calls. Untangle shows no suggestion. | Nobody |
| My Claude Code | Settings shows the `claude mcp add` command with the user's personal key. They run the `pr-triage` skill in their own Claude Code, which writes verdicts and proposes strings through MCP. | The user, on their own plan |
| My Anthropic key | The user pastes a key; it is encrypted in `user_ai` and used only for their board, with their own monthly budget. | The user |
| Company key | Only for users an admin grants it to. Uses the server's `ANTHROPIC_API_KEY` with a per-user monthly budget enforced by the existing reserve-before-call check. | Whoever owns the key |

The existing caching, budget events, per-repo AI switch and Evaluate page work unchanged inside each user's database. The server-wide `Ai:Enabled` switch stays as a kill switch for all modes that call Anthropic.

## Security

- **Isolation is the main property.** Every route resolves a user before touching data; there is no code path that takes a user id from the request body or URL. A test signs in as two users and proves neither can read the other's board, events, MCP results or actions.
- **OAuth flow.** `state` checked on callback; PKCE with S256; the callback URL is fixed in configuration; failed or replayed callbacks show an error and store nothing.
- **Cookies.** HttpOnly session cookie, Secure off localhost, 8-hour lifetime with sliding renewal, re-checked against the allowlist and the `disabled` flag on every request.
- **Host and Origin.** The current check stays, with allowed hosts from configuration (#16).
- **Secrets.** GitHub tokens and personal Anthropic keys are encrypted at rest; personal keys are stored as hashes; Data Protection keys live outside the databases.
- **Rate limits.** Login, callback and personal-key failures are rate-limited per IP.
- **Deletion.** "Delete my Tangle" removes everything for that user and can't be undone; it asks for the login to be typed as confirmation.

## Milestones

Same rules as the Implementation order tab: one branch and one PR per milestone, stop at each lettered checkpoint, record decisions in `docs/DECISIONS.md`. Checkpoints continue the lettering after J. The open bugs #11–#14 and #16 should be merged first.

### G0 Decisions

**Checkpoint K: Metju answers the decision table.** The agent writes the answers into DECISIONS and, if a GitHub App was chosen, the exact permissions to request. Metju has registered the app (see Using the registered app). The agent records the slug and configuration keys in DECISIONS and asks Metju to tick the open items there, including installing it and putting the client ID and secret in user-secrets.

### G1 Per-user databases behind the local key

- [ ] Control database with its migrations; `UserDatabases`; `IUserContext`; stores moved off the singleton `Database`.
- [ ] `Auth:Mode=Local` maps the development key to one fixed local user; behaviour is otherwise unchanged.
- [ ] Personal keys moved to the control database (forward migration that copies existing hashes).
- [ ] Events, sync lock and AI queue keyed by user.

**Done when:** every existing test passes in local mode; a new test with two users in the control database proves isolation of board, events and MCP.

**Checkpoint L: nothing changed for Metju.** He runs his normal board for a day in local mode.

### G2 GitHub sign-in

- [ ] `Auth:Mode=GitHub`: login, callback, session cookie, sign out, allowlist, "not on the list" page, login screen button.
- [ ] Account section in Settings.

**Done when:** Playwright signs in through a fake GitHub OAuth server; allowlist and replayed-state cases are refused.

**Checkpoint M: Metju signs in with GitHub on localhost.** He checks the allowlist with a second account.

### G3 The user's token everywhere

- [ ] `GitHubTokens` returns the signed-in user's token in GitHub mode; refresh, revocation banner, per-user locks.
- [ ] Sync, Apply and inspection use it; Apply allowlist per user.

**Done when:** tests cover refresh, expiry and a 401; a sync for user A never uses user B's token.

**Checkpoint N: a real sync with his own token.** Metju compares the board with his local-mode board; Apply still only in the sandbox.

### G4 Importing today's data

- [ ] A one-off command copies `tangle.db` to `data/users/<metju's id>.db`, keeping every string, note, sticky, position, decision and AI call, after a backup.

**Checkpoint O: Metju's board looks the same after sign-in.**

### G5 AI modes, account deletion, admin page

- [ ] The four AI modes in Settings; encrypted personal Anthropic key; per-user budget; admin grant for the company key.
- [ ] Delete my Tangle; admin page with allowlist and users.
- [ ] Docs: README, user guide, `setup-claude-code.md`, Help panel.

**Checkpoint P: a second person uses it.** A colleague signs in, syncs, tries "My Claude Code", and deletes their Tangle; Metju checks that nothing of theirs remains.

## Testing, risks and open questions

**Testing.**

- A fake GitHub OAuth server in the test host (authorize, token, refresh, `/user`, org membership), so no test talks to GitHub.
- An isolation suite run in every milestone from G1: two users, every API route, the event stream and every MCP tool.
- Migration tests over the control database and over an imported copy of a real user database.

**Risks.**

| Risk | Mitigation |
| --- | --- |
| TALXIS won't install a GitHub App, so its PRs are invisible | Decide at checkpoint K; fall back to an OAuth App |
| A store keeps a cached database and leaks data across users | No singleton store holds a `Database`; the isolation suite runs on every PR |
| Backups of many SQLite files | Before hosting, confirm how the current Litestream release handles a growing set of files; otherwise a nightly snapshot of `data/` |
| Token refresh races | Per-user refresh lock; tests with concurrent requests |
| Lost Data Protection keys make every token unreadable | Keys backed up with the data; losing them only forces everyone to sign in again |

**Open questions.**

- Should a user be able to export their board (one SQLite file download) before deleting it?
- Does the admin need to see AI spend per user for the company key, or only the total?
