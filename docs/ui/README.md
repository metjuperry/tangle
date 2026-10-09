# Tangle UI reference

Screenshots of the clickable prototype ([live version](https://claude.ai/artifact/L3bDk2rXBtoX68vqCiYsnx)), captured at 1640×1200 (1640×1800 for 07). These are the **visual reference** for M3 (board), M4 (strings, notes), M5 (AI suggestions) and M6 (Untangle, receipt). The Technical design is the source of truth for behaviour; where a screenshot and the design disagree, the design wins.

**About the data.** PR titles, numbers, authors and open dates are real (TALXIS repos, read 8 Oct 2026). Whose turn, verdicts and strings are illustrative, except alm-lab #49 (4 unanswered review comments) and tools-cli #195 (the author's own review asks for a rework), which come from the PRs themselves.

**About the stack.** The prototype is hand-built absolute-positioned HTML, not React Flow. Copy the look (colours, type, card anatomy, string styles, stamps, panel layout), not the implementation. Visual tokens are listed in `02-technical-design.md` → Frontend → Visual tokens.

## Board

| File | State | What to take from it |
| --- | --- | --- |
| `01-board-card-back-49.png` | Default board, alm-lab #49 selected | Repo zones as dashed regions with a taped label (`repo · N yours, M others`). Cards: number + age top row, two-line title, `agent` tag, rotated stamp (YOUR TURN / STALE / BATCH / BOT). Pin colour = whose turn (orange you, blue them, grey nobody). Aged paper past 40 days. Batches render as a stacked card (`#98–121`). Legend bottom-left. Header: Board/Untangle toggle, counts, knot meter (“Untied 0 of 13”), others toggle, queued count. Side panel = the card back: ref, author, age, turn chip, session chip, **Suggested next step** block with *Queue it* / *Decide in Untangle*, Why it exists, Open asks (author · date · text). |
| `02-card-back-195.png` | tools-cli #195 selected | Card back of a PR whose open ask is the author's own review. “On the same string” lists linked PRs with their link status (`suggested`). |
| `03-ai-suggested-string.png` | Clicked a `?` tag on a dashed string | String panel for an AI suggestion: “Do these belong together?”, the AI's reason, both PRs, *Tie it* / *Cut the string*. |
| `04-tie-mode.png` | Pressed “Tie a string to another PR” on #49 | Navy banner “Pick the card to tie alm-lab #49 to”, with Cancel; every card gets a navy outline as a pick target. Keyboard path for tying (the real app also supports dragging pin to pin). |
| `05-new-string-needs-label.png` | Picked #167 as the other end | New navy string; panel asks for a label (“so future you remembers”). |
| `06-string-tied-and-labelled.png` | Saved the label | “Tied by you” panel: the label in Caveat handwriting, Edit, explanation that the AI reads it and it survives syncs. Navy string with a pencil tag on the board. |
| `07-card-back-with-note.png` | Pinned a note on #49 (tall viewport) | Bottom of the card back: user-tied string listed with its label, “Your notes” in handwriting, note input + *Pin it*. |
| `08-others-dimmed.png` | “Dim others' PRs” on | Other people's PRs fade; yours stay full strength. Button flips to “Show others' PRs”. |

String styles: **solid red** with ✓ tag = rule link, **dashed red** with `?` tag = AI suggestion awaiting judgement, **solid navy** with ✎ tag = tied by you. Strings sag and are drawn above cards with a soft shadow.

## Untangle

| File | State | What to take from it |
| --- | --- | --- |
| `09-untangle-deck.png` | Untangle view, first card | One big card on a cork background with a small stack behind it; progress pill (“Card 1 of 13 · your turn first, then the dustiest”); suggested verdict block on the card; six verdict buttons with key hints (Merge M, Fix F, Ping P, Wait W, Close X, Snooze S), the AI's pick filled dark. Receipt (“TANGLE APPLY”) on the right. |
| `10-untangle-receipt-filling.png` | After four decisions | Knot meter filling; receipt lists queued actions as a till roll with a total. |
| `11-untangle-done.png` | Deck finished | “All 13 untangled” card with *Back to the board* / *Start over*; full receipt. In the real app the receipt carries the **Apply** button (one confirmation per action). |

## Source

`source/*.dc.html` is the prototype markup and logic (state, sample data, interactions). It needs the Design canvas runtime to run, so treat it as readable reference: the `renderVals()` class at the bottom holds the sample cards, strings, verdict order and handlers.

Both the screenshot `11` and `source/Main.dc.html` say “Press Apply on the receipt” at the end of the deck. The live prototype still says “Run `tangle apply`” from before the CLI was dropped; there is no CLI.
