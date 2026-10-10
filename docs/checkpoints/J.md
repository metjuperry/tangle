# Checkpoint J — Local polish and explanation

2026-10-10. **Stop here for normal-week review. Azure M9 is deferred.**

## Built

- Help opens a feature explanation panel covering the board, search, strings,
  groups, notes, Untangle, receipt rounds, provider privacy, evaluation and sessions.
- Help and the generated [user guide](../user-guide.md) share
  `docs/features.json`. CI checks that documentation is synchronized.
- README now describes the app, local setup, controls, safety, tests and links.
- Ctrl+K/Cmd+K search brings a matching PR into view. Cards become compact below
  60% zoom and zones summarize below 35%; clicking a summary zooms into its repo.
- Keyboard tying through a labelled pin button and card back; directional arrows
  focus the nearest card. Reduced motion remains respected.
- Automated axe checks pass the board, card back and Untangle deck.

## Scope changed by Metju

M9 deployment is deferred; no Azure resources or Terraform changes were made.
Metju also deferred demo data and prioritized feature explanation/documentation.
No `--demo` dataset or browser offline cache is shipped. Normal test fixtures
remain isolated and anonymized. The local app renders stored SQLite data first
and retains the existing in-app loading overlay.

## Run and demo — 10 minutes

Build `web/`, restart the host and open Help.

1. Read the sections explaining Wait/Snooze, Still my turn and Tear & save.
2. Use Ctrl+K/Cmd+K to find a PR; choose it and inspect its back.
3. Zoom out through compact cards to repo summaries; pick a summary.
4. Tab to a card and use arrow keys to move focus. Use its pin/button to enter
   keyboard tie mode.
5. Compare README/user guide with the UI and identify any confusing language.

## Validation and limitations

Browser tests cover search, summaries, keyboard focus, feature help and axe.
The complete fixture suite also covers authoring, receipt, sessions and evaluation.
No paid AI call or GitHub backlog action was performed for this milestone.

All nodes remain mounted so off-screen cards can participate in keyboard
navigation and search. This deliberately differs from onlyRenderVisibleElements;
hundreds of cards may need virtualization with a keyboard-aware focus mechanism
later. Semantic zoom currently hides details without changing stored positions.

The complete accessibility behavior should still be reviewed manually with your
usual keyboard/screen reader. Native Windows session-hook validation remains
outstanding from M7.

## What to decide

Use the app for a normal week. Note what you use, skip, find confusing or want
changed. Deployment remains a later decision; no next milestone is started.
