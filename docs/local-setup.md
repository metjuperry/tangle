# Local setup (M0-M1)

Requires .NET 10, Node 24, npm, and GitHub CLI for M1 sync.

```sh
cd web
npm ci
npm run build
cd ..
dotnet user-secrets set "Tangle:DevelopmentKey" "<random key of at least 32 characters>" --project src/Tangle.Host
dotnet run --project src/Tangle.Host
```

Open http://127.0.0.1:5202 and enter the development key. The shell is public but every data API requires authentication. The key is exchanged for an HttpOnly, SameSite=Strict session cookie; API clients can use `Authorization: Bearer <key>`.

Environment equivalents: `Tangle__DevelopmentKey`, `Tangle__Address` (default `http://127.0.0.1:5202`), `Tangle__DatabasePath` (default `~/.tangle/tangle.db`). Use a separate database path for experiments. The app only applies forward migrations; it never resets a database.

Build the web **before** building .NET; `Tangle.Web` embeds the built assets. Rebuild .NET after changing the web.

```sh
dotnet test
cd web
npm run lint
npx playwright install chromium
npx playwright test
```

Browser tests start their own loopback host using a fresh temporary database, never the working database. No tests call GitHub or an AI provider. A remote is not configured yet, so GitHub Actions cannot run until a repository destination is supplied.

## GitHub sync

Run `gh auth login` if needed. The backend reads `gh auth token`; it never stores it. For fine-grained credentials use `GitHub:Tokens:TALXIS` and `GitHub:Tokens:metjuperry` in Host user-secrets (environment equivalents `GitHub__Tokens__TALXIS` and `GitHub__Tokens__metjuperry`). A single fallback credential may be set as `GitHub:Token` / `GitHub__Token`. Tokens need read access to the watched repositories and pull requests; no write permissions are needed in M1.

Open `/settings`, add `TALXIS` as an Organization and `metjuperry` as a Personal account, then **Save settings**. Press **Sync** and open `/debug/prs`. Defaults are empty until you configure them. Owner scopes include PRs involving your authenticated GitHub account, not every open PR in an organization. AI cannot be enabled in M1.

`POST /api/sync` returns fetched/changed/call/cost counts. Cost includes search and nested pagination calls; viewer lookup cost is logged separately. No automatic or scheduled sync is implemented in M1. The debug page includes cached closed PRs for inspection; `/api/board` and the Board page exclude them.
# Configurable ingress (no deployment)

`Auth:Mode=GitHub` uses the registered GitHub App browser flow. Follow
[checkpoint M](checkpoints/M.md) for client ID/secret and exact callback setup.
The first user-token sync and old-board import are G3/G4, not part of G2.
`Auth:AllowedLogins` and optional `Auth:AllowedOrganizations` are server-side
admission lists. Data Protection keys persist under `keys/` beside the configured
database, or `Auth:KeyDirectory`; keep that folder private and backed up.

`Auth:Mode` defaults to `Local`; the development key and existing personal-key
routes remain unchanged. Non-default bind hosts require exact
`Tangle:AllowedHosts` (including port where used) and `Tangle:AllowedOrigins`.
Wildcards are not supported.

`Auth:Mode=ContainerApps` accepts `X-MS-CLIENT-PRINCIPAL-NAME` only when the
connection originates from an explicit `Auth:TrustedProxyAddresses` IP and the
login is in `Auth:AllowedLogins`. Configure ingress to remove client-supplied
identity headers and prohibit direct backend access. Never trust a principal
header from arbitrary clients. Local mode ignores these headers; personal keys
still exclusively protect MCP/session reporting. No Azure resources are created.
