# Claude Code and Tangle (M7)

Build the web, run `dotnet run --project src/Tangle.Host`, and open
http://127.0.0.1:5202/settings using your development key.

## Personal key

Create an **Agent personal key** with a descriptive name. Copy its value once;
only its SHA-256 hash is stored in SQLite. Keep the value in your environment or
secret manager, never source code. Settings can revoke it immediately.

Personal keys authenticate `/mcp` and POST `/api/sessions` only. They cannot
access Settings or the Apply endpoint. The development key and browser cookie
cannot substitute for a personal key on those routes.

```sh
export TANGLE_PERSONAL_KEY="<personal key>"
export TANGLE_URL="http://127.0.0.1:5202"
claude mcp add --transport http tangle "$TANGLE_URL/mcp" \
  --scope user --header "Authorization: Bearer $TANGLE_PERSONAL_KEY"
```

The `claude mcp add` command stores connection credentials in Claude Code's user
configuration; protect that file and revoke unused keys. Do not commit it.

## SessionEnd hook

Requires Node.js 24 and Git on PATH on both platforms. The wrappers use the same
Node implementation to parse JSON safely and avoid platform-specific payload
escaping. Transcripts remain local; only the transcript path is sent by default.

Add to your own `~/.claude/settings.json` without replacing existing hooks:

```json
{
  "hooks": {
    "SessionEnd": [{
      "hooks": [{
        "type": "command",
        "command": "/absolute/path/to/Tangle/hooks/tangle-session.sh"
      }]
    }]
  }
}
```

On Windows use `pwsh -File C:/absolute/path/to/Tangle/hooks/tangle-session.ps1`.
Ensure the Claude Code process inherits `TANGLE_URL` and `TANGLE_PERSONAL_KEY`.
The script reads SessionEnd JSON on stdin, finds the cwd's GitHub origin and
current branch, and POSTs metadata. HTTP/parse/Git failures are explicit.

First-prompt excerpts are **off**. To opt in, set
`TANGLE_SESSION_EXCERPT=true`; the first user message is collapsed to a single
line of at most 300 characters. This may contain sensitive text; leave it off
unless you intend to store that excerpt in your Tangle database.

## Matching and tools

Sessions match a unique open PR by GitHub repo and head branch. A
`Claude-Session:` trailer creates a direct session association; manual
attachment on the card back wins over later automatic matches. Ambiguous branch
matches remain unattached. Unmatched reports can be attached from a card back.

MCP reads: `list_prs`, `get_pr_context`, `get_threads`, `get_eval_report`.
MCP proposals: `add_note`, `propose_link`, `record_verdict`, `queue_action`.
There is **no Apply tool**. `get_eval_report` reads saved evaluation metrics
without a provider replay. Other stale reads refresh GitHub on demand, potentially
starting the normal AI pass only if you already enabled it for those repos.

The repository's `.claude/skills/pr-triage/SKILL.md` describes the safe triage
workflow. It is project-scoped; copy it to your personal Claude skills directory
if you want it available from other projects.
