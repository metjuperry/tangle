# Checkpoint E — Opt-in AI proposals

2026-10-10. **Live validation is blocked pending your explicit AI opt-in and paid-call authorization. M6 must not start.**

## Built

Published as [PR #6](https://github.com/metjuperry/tangle/pull/6), based on M4 and left unmerged. Offline validation passed 75 .NET tests and 17 browser tests. The live requirements below remain blocked; no provider call was made.

M5 adds the official `Anthropic` C# SDK, `claude-haiku-5-5`, structured output schemas, versioned prompts, the design's `IDecider`, bounded PR context and proposed verdicts/intent. Pair candidates use shared title words, folders or author/date; sticky membership is also judged. Pairs are sent in batches of at most 20, and suggestions appear only above 0.75. Existing strings, including cuts, are skipped.

Repo AI switches default off. Context includes only enabled repositories; disabled linked PRs and their labels are excluded. No diffs or file contents are sent. The server also defaults `Ai:Enabled` off. All implementation validation used synthetic responses with a fake HTTP handler or local fixtures; **no real AI call was made**.

Successful results are cached by model, prompt content and complete bounded input. Verdicts and `?` strings appear through query invalidation. You tie/cut proposals; nothing applies to GitHub.

 Monthly USD budget defaults to $1 and is editable in Settings. Calls reserve conservative maximum cost before dispatch, record tokens/cost/latency, and retain a reservation when an interrupted call's usage is unknown. Recent call status is shown in Settings. An exhausted budget stops provider calls and emits a warning.

## Run safely first

Settings is organized into bordered sections for owners, repository permissions, AI privacy/budget, bot detection and recent calls. Each repo has independently labelled Watch and Allow AI controls; the page has one save bar at the bottom. No permission is changed just by this layout update.

The save bar now remains visible below an independently scrolling settings area. AI history is collapsed by default and shows its entry count; expanding long histories does not push saving off-screen. Keyboard-accessible disclosure and mobile layouts are supported.

Call history now records the job, assessed PR/pairs, and outcome (verdict or number of new suggested strings). PR assessments link to their card. Forward migration 0005 recovers prior verdict outcomes where their input hash matches; older pair calls without stored targets remain explicitly unidentified rather than guessed. This improvement makes no provider calls.

```sh
cd web
npm ci
npm run build
cd ..
dotnet run --project src/Tangle.Host
```

Leave `Ai:Enabled` unset/false. Inspect Settings: privacy notice, AI switches and monthly budget. This makes no provider calls even if an API key happens to be present.

Forward migration 0004 adds queue accounting and input cache tables; existing working data is not reset.

## Before the first real call — Metju must decide

1. Choose the repos you permit to send PR metadata and your linked labels/sticky titles to Anthropic. Check client contracts/policy first.
2. Put **ANTHROPIC_API_KEY** in an environment variable, or Host user-secrets:
   `dotnet user-secrets set "ANTHROPIC_API_KEY" "<key>" --project src/Tangle.Host`.
3. Authorize paid API use by setting `Ai:Enabled` to true in Host user-secrets (environment `Ai__Enabled=true`), and choose a monthly budget in Settings.
4. Restart the host, explicitly enable only chosen repo switches and save.
5. Press Sync. Facts appear first; AI proposals follow through live events.

The implementing agent stops before doing these steps. Enabling AI is not implied by approval of checkpoint D or a request to implement M5.

## Demo — 10 minutes after opt-in

Read next-step proposals on ten cards, compare reasons with their computed facts, and inspect every dashed `?` string. Tie or cut each proposal; verify those decisions survive sync. Inspect recent call costs in Settings. Sync again unchanged: cached verdicts and candidate answers should avoid repeated calls.

## Deviations and open questions

AI reuse is now tracked per PR and per candidate pair, independently of request batches and prompt versions. Unchanged GitHub metadata and authored context do not trigger another call: clock-only age/idle changes, startup, sync and prompt edits are not automatic rerun triggers. Changed PR metadata, labels or sticky context can require a new judgment. Negative pair answers are retained even when no string is created. Paid/uncertain failed attempts are also remembered to prevent repeated charges for unchanged inputs; known zero-cost pre-dispatch failures can be retried.

Forward migration 0009 adds look-state records without deleting existing answers. Identifiable successful prior calls are adopted when PR timestamps and authored context show no later change; older pair logs without targets cannot be reliably recovered. The Settings history shows only the latest 50 calls, so 50 displayed rows alone does not establish 50 calls on the last sync.

Pair outputs now use schema-constrained numeric indices rather than asking the model to copy PR/sticky references. Tangle maps each index to its exact input pair, accepts reordered results and rejects duplicate/missing answers. Existing cached successful results remain stored; the new pair prompt/input format has a new cache key. No paid retries are triggered by the implementing agent.

Prompts now request brief, plain-language reasons and intent, without internal field names. Existing AI text is displayed with known schema terms translated (for example behindBy → commits behind base); raw saved answers and your own labels/notes are unchanged. Prompt edits change cache keys, so a later user-triggered sync may regenerate allowed-repo judgments under the normal budget.

Pair requests are now packed by serialized UTF-8 payload size as well as the 20-pair maximum. Large repeated PR contexts are split into smaller requests instead of aborting the pass. This 60 KB per-request bound is separate from the monthly USD budget. Requests rejected before dispatch have zero usage; migration 0007 releases only reservations bearing the exact old pre-dispatch size error. Unknown network usage remains conservatively reserved.

- The model and pricing were checked against current Anthropic documentation. Base pricing used is $0.10/M input tokens and $0.50/M output tokens; requests are bounded below the 100k-token tier.
- Conservative per-call reservations are $0.02 for verdicts and $0.20 for a pair batch. Unknown interrupted usage remains reserved rather than silently treated as free.
- Worker errors are explicit, do not alter facts, and do not create success-shaped fallback answers. SDK automatic retries are disabled.
- There is no live recorded Haiku fixture yet; synthetic schema-matching fixtures cover the adapter offline. Live schema acceptance, actual cost and judgment usefulness remain unverified until opt-in.
- AI notes are not included: the design's bounded context specifies string labels/sticky titles but not free-text notes.
- Current latest verdicts are retained as history and displayed; if facts change, an old verdict can remain until its replacement arrives. Review timestamp/context when judging it.

Decide the 0.75 threshold, prompt improvements, allowed repos and budget after a real run. **M5's live Done-when requirement and green remote CI cannot be claimed yet.**
