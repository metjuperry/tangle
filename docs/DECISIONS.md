# Decisions

| Date | Question | Answer |
| --- | --- | --- |
| 2026-10-10 | There is no Git repository or remote. | Initialize a local repository, preserve the supplied docs on `main`, and use `milestone/m0` and `milestone/m1`. Do not create a remote repository without a destination. PR creation and remote CI are blocked until a remote is supplied. |
| 2026-10-10 | How does a browser supply the local development key? | A same-origin login form exchanges the key for an HttpOnly, SameSite=Strict cookie. API clients may also use a Bearer key. Check Host and Origin before authentication; never put the key in a URL or browser storage. Only the login shell and liveness probe are public. |
| 2026-10-10 | The schema omits watched organizations, reviewer identity and commit authors needed by later rules. | Add an `organization` settings table, `pr_review_request` cache table, and keep typed event details as JSON in `pr_event.detail`. Include the documented `ai_call` log. No facts or AI behavior is implemented before its milestone. |
| 2026-10-10 | The design mentions config.json, while M1 requires settings in SQLite. | Watched scope settings live in SQLite per M1; tokens and runtime settings come from environment variables or user-secrets. Defaults start empty until explicitly configured. The `involves:@me` scope includes authored and review-requested PRs pending checkpoint A. |
| 2026-10-10 | Can M0 seed demo data when full demo mode is M10? | M0 browser tests use a separate temporary database and development key. Do not implement demo mode or touch the user's working database. |
