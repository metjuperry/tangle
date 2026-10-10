# Recorded GitHub fixtures

`github-pr-page.json` is a public read-only GraphQL response from alm-lab #49, recorded on 2026-10-10. Names, titles, bodies, commit messages, paths, node IDs and cursors are anonymized. Dates, counts, review states and connection shapes are retained. Tests consume only this file, never the network.

To intentionally refresh it using your authenticated GitHub CLI:

```sh
node tests/fixtures/record-github.mjs
```

Review both the JSON and the Verify snapshot before accepting changes. The recorder requests only PR metadata, not diffs or file contents, and never calls an AI provider.
