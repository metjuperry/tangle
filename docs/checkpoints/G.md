# Checkpoint G — MCP and session context

2026-10-10. **Stop here: M8 requires Metju's approval.**

## Built

- Official ModelContextProtocol.AspNetCore 1.0 HTTP transport at `/mcp`.
- Read tools `list_prs`, `get_pr_context`, `get_threads`, `get_eval_report`; proposal tools `add_note`, `propose_link`, `record_verdict`, `queue_action`. **No Apply tool.**
- Read tools refresh stale GitHub cache on demand. All proposals write to the shared services/store and send the same live invalidations as browser edits.
- Personal keys created/revoked in Settings, stored only as hashes. MCP and session POST require these keys, not browser cookies or the development key. Personal keys cannot reach settings or Apply APIs.
- Session reporting by GitHub origin/head branch, direct Claude-Session trailer matching and manual attachment on card backs. Manual choices win; ambiguous branches/trailers are not guessed.
- Shell and PowerShell SessionEnd wrappers share a Node implementation. Transcript bodies stay local; an optional first-prompt excerpt is off by default.
- Project `pr-triage` skill and [Claude Code setup](../setup-claude-code.md).

## Run

Build the web, restart the host and open Settings. Create an **Agent personal key**.
Use its one-time value as `TANGLE_PERSONAL_KEY` and follow the MCP/hook setup doc.
No new Anthropic key or paid evaluation is required.

Forward migration 0011 adds hashed keys and session metadata columns. It does not
reset the database. Do not commit personal keys or Claude user settings.

## Demo — 10–15 minutes

1. Connect Claude Code to `/mcp` with a personal key.
2. Ask “What is my turn today?” and “What blocks alm-lab #49?”
3. Ask it to propose a specific business string with a reason. Verify it appears
   dashed, ready for you to tie/cut; existing ties/cuts remain intact.
4. Ask it to add a note or queue a draft comment. Verify the board updates live;
   no comment is sent until browser Apply confirmation.
5. Configure SessionEnd in one repo with an open PR. End a session and inspect
   its card back. If unmatched, attach it manually.
6. Revoke the personal key and verify MCP/session reporting is rejected.

## Verification and limitations

Published as [PR #8](https://github.com/metjuperry/tangle/pull/8), based on M6 and left unmerged. Local validation passed 95 .NET tests, 35 browser tests and two hook-wrapper tests. GitHub reports no workflow runs for the branch, so remote CI is not claimed green.

Local tests exercise authenticated HTTP MCP initialize/list/call, all eight tool
methods, personal key hashing/scope/revocation, branch/trailer/manual matching,
session reporting and browser key controls. Hooks use temporary real Git repos
and loopback HTTP; both Bash and PowerShell wrappers pass on macOS, with and
without first-prompt excerpts.

**A native Windows hook run and the user's Claude Code connection are not yet
verified.** PowerShell behavior was exercised with pwsh on macOS, not claimed as
Windows validation. The `get_eval_report` tool explicitly reports M8 unavailable;
it does not fabricate a report or call providers.

Remote CI remains unverified unless GitHub registers/completes a run. No real
GitHub backlog mutation, paid AI call or Azure resource was created during M7.

## Decisions

Session cards use a readable Claude Code session label, report date/branch and optional excerpt rather than a raw URL. Recognized HTTPS Claude links have an Open session action; raw IDs/URLs are available in expandable details. No link is invented for local session IDs and no transcript is fetched.

- Excerpt off by default: decide whether you want the first prompt stored.
- Session paths are metadata, not browsable transcript access; Tangle never
  opens a reported local path.
- The shared Node hook requires Node 24 and Git on PATH on Windows as well as
  macOS/Linux, consistent with the app's existing tooling.
- The MCP endpoint uses stateless HTTP transport; keys are checked per request,
  so revocation takes effect on the next call.
- `queue_action` uses the same queue validation as the browser, including a
  read-only head lookup for merge. Agents never have an Apply operation.
- Ambiguous branch or repeated cloud-session associations are left unassigned,
  rather than attributing a session to the wrong PR.

Review the skill workflow and session matching, then approve G. **M8 has not started.**
