# Checkpoint B — Facts and rule strings

2026-10-10. **Stop here: M3 requires Metju's approval.**

## Built

M2 is on local branch `milestone/m2`, based on the approved M1 branch. Nothing is posted to GitHub.

- Pure Core functions implement the eight ordered whose-turn rules, reasons, human idle days, age, conflicts, XS–XL size and human/agent/bot origin.
- Bot logins are saved in Settings. Review-thread replies count as human activity; CI and configured bots cannot reset human idle.
- Deterministic batches group five or more open PRs from one repo and author within an inclusive 14-day window, using normalized title patterns.
- Rule strings detect explicit PR references in bodies/comments/commits, same-repo stacks, and exact changed-file overlap.
- Facts, batches and strings update inside the cache transaction at sync, including unchanged syncs. Startup computes facts for the existing cache. Changing bot settings recomputes facts without a GitHub call.
- `/api/board` now includes facts, batches and visible strings; `/debug/prs` shows turn, reason, age/idle, size/origin and batch, with a rule-string list.
- Cut strings, user ties, labels, notes, stickies, positions and queued actions survive sync.

## Run

Use the existing development key and GitHub login; no new secret is required.

```sh
cd web
npm ci
npm run build
cd ..
dotnet run --project src/Tangle.Host
```

Open http://127.0.0.1:5202/debug/prs and unlock it. If a host from your A review is still running, stop that specific host first and restart with M2.

Migration `0003_facts_inputs.sql` adds viewer identity and the bot-logins setting. Migrations only move forward; no working database is deleted or replaced. Startup infers viewer identity for old M1 records from the author or saved scope. If it cannot, it reports the missing identity rather than inventing one.

## Demo — 10–15 minutes

1. Open `/debug/prs`, press Sync and read your PRs' turn/reason columns.
2. Check alm-lab #49: it should say `me`, `4 open review comments`, and `agent`.
3. Compare drafts, failing checks/conflicts, requested changes and approved-green PRs with GitHub. Check that the first applicable rule wins.
4. Inspect dusty PRs: idle is time since human activity, not the latest GitHub update. A recent bot comment must not make an abandoned PR look active.
5. Read the rule-string list. Check explicit references, stacks and shared-file links against what you know. Strings only connect open PRs in the watched board.
6. Open Settings and inspect the bot logins. Add a bot used in your repos if needed; saving recomputes existing facts without a network sync.
7. If your watched scope contains the scaffold-applier family, check that matching PRs share a batch. The fixture covers this family even if those PRs do not involve your account.

## Evidence

On the isolated M1 validation database, migration preserved all **17 open PRs**, all 17 gained facts, and **six rule strings** were visible. alm-lab #49 computed **me / 4 open review comments / agent**. A subsequent unchanged two-owner sync returned zero changes and used four calls / four GitHub rate-limit points.

Recorded public scaffold metadata has **33 matching applier PRs**, all from one anonymized author within 14 days. They form one batch; two unrelated search hits do not join it. The recorded #49 response and full fact snapshot confirm its four unresolved asks. Unit/API/browser coverage includes exact rule precedence and thresholds, origin markers, same/cross-repo references, stacks, overlap, batch boundaries, durable cuts, stable strings on repeated sync, bot-setting recomputation, and transaction rollback when fact inputs are invalid.

No AI requests, GitHub mutations, Azure provisioning or paid operations were made.

## Deviations and open decisions

See [DECISIONS](../DECISIONS.md):

- **Idle threshold:** 21 days for nobody's turn. Confirm or change it.
- **Aged paper:** 40 days remains the design default; paper rendering begins in M3. Confirm it before visual work.
- **Size:** initial cutoffs are additions + deletions: XS ≤10, S ≤100, M ≤500, L ≤1000, XL >1000. The design did not specify boundaries.
- **Batch:** five PRs, same repo/author, inclusive 14 days. Identifiers are stripped; the exact scaffold-applier title family also normalizes its varying template names. Confirm that this groups the work as you would.
- **Unknown behind-base count:** `behindBy` stays null, not zero. Decide whether a later read-only comparison query is worth adding; it is not present in the named sync query.
- **Human identity:** unknown actors do not count as the last human actor. Unresolved asks require a named, non-bot actor other than you. A missing changes-requested review timestamp conservatively leaves the PR as your turn.
- **Rule precedence:** one string per pair: explicit reference, then stack, then file overlap. User choices always win.

An uncommitted local edit in `docs/local-setup.md` may contain a development key. It was left untouched and **excluded from the milestone commit**. Remove it before publishing documentation; if it has been shared, rotate the key.

## Review and publication

For each of your PRs, mark whether turn and reason are correct. Any disagreement should become a rule fix or recorded decision before M3.

**Remote delivery remains blocked:** no GitHub remote destination has been supplied. M0, M1 and M2 are separate local milestone branches, but milestone PRs and green GitHub CI cannot be claimed. Supply the intended remote and publication authorization; no branch will be merged without explicit approval.

Approve checkpoint B after reviewing facts and thresholds. **M3 will not start until approval.**
