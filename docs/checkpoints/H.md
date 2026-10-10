# Checkpoint H — Evaluation stats and the Jev slot

2026-10-10. **Stop here: M9/Azure requires approval. No provider calls were made during implementation.**

## Built

Published as [PR #9](https://github.com/metjuperry/tangle/pull/9), based on M7 and left unmerged. Local validation passed 100 .NET tests and 38 browser tests. GitHub CI has now registered a queued run; its result is not yet known.

- Evaluate page and downloadable Markdown report from saved model judgments and your labelled decisions.
- Relationship precision/recall at strictly above 0.75; next-step agreement; Brier calibration; cost/latency per 100 completed calls; disagreement lists.
- Coverage count and explicit “more needed for 30” message. Snooze is excluded because it postpones a decision rather than selecting a model verdict.
- Saved rounds still count as labelled decisions. Historical next-step predictions are chosen from before the decision, never a later judgment.
- Original bounded input snapshots and negative pair predictions are now recorded for future replay. Old inputs are not fabricated.
- Explicit, budgeted evaluation replay using original inputs from currently AI-allowed repos. Replays are stored separately, never overwrite the board, skip already replayed examples and do not run automatically.
- Per-job decider choice in Settings; Haiku remains default. Jev is disabled separately on the server.
- Jev HTTP adapter against the verified TypeSafe Choice/Score/Noul contract, using `jev-latest`; tested with documented-shape synthetic fixtures only.
- MCP `get_eval_report` now reads real stats without syncing or calling providers.

## Run and demo — 10 minutes

Build web and restart the host as usual. Open **Settings → Open Evaluate**. Evaluation is not in the main header; recent AI calls now live in a collapsible history on Evaluate rather than on Settings.

1. Read the labelled-example coverage. If there are fewer than 30, keep triaging rather than trusting small-sample metrics.
2. Read Haiku agreement and string metrics. Expand disagreements and compare with what you know.
3. Download the Markdown report.
4. Check cost per 100 calls and latency. These are completed-call averages, not your remaining budget.
5. Confirm merely opening Evaluate makes no provider calls.
6. Only if you want a paid comparison, authorize its checkbox and run replay. Existing examples without original snapshots cannot be fairly replayed.

## Jev setup — optional, never automatic

Store your key as `TYPESAFE_API_KEY` in the environment or Host user-secrets:

```sh
dotnet user-secrets set "TYPESAFE_API_KEY" "<key>" --project src/Tangle.Host
dotnet user-secrets set "Ai:JevEnabled" "true" --project src/Tangle.Host
```

`Ai:Enabled=true` is still required for provider calls. Existing per-repo AI permissions and monthly budget still apply. Restart, then select Jev for an evaluation replay; **do not switch production jobs merely because the key exists**.

Jev provides decisions/probabilities, not written explanations. Its verdict falls back to the computed whose-turn reason; **Explain with Haiku** on a card is an explicit paid request for written reason/intent. Written explanations stay assigned to Haiku in Settings.

## Interpretation and limitations

- Precision/recall cover only the labelled set. Because current labels mostly originate from suggestions, this is not a claim about finding every true relationship in the entire backlog.
- Verdict Brier scores confidence in the selected action against agreement, not full multiclass probabilities (Haiku supplies no complete distribution).
- Snooze counts are shown but excluded; Still my turn counts as Fix.
- The current schema holds only the latest tie/cut status per string, so it is one labelled example per decided pair, not a history of every reversal.
- Historical verdicts/links can be scored without original inputs; fair cross-provider replay requires snapshots recorded from this milestone onward.
- Jev pricing verified from current docs: $0.042 per million input tokens; output free. Score levels are zero-indexed, mapped to priority 1–5.
- Synthetic Jev fixtures match the documented contract but are not claimed as live provider recordings. Jev live validation and 30 real user labels are checkpoint tasks.
- Unknown/uncertain interrupted replay usage remains budget-reserved and is not retried silently.

## Review

Judge the disagreements, decide prompt changes, and decide whether to compare Jev on the same labels. Supply at least 30 examples for an initial quality judgment; if fewer, the page reports the shortfall. **M9 has not started.**
