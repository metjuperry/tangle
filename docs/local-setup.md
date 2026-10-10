# Local setup (M0)

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
