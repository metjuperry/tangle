# Checkpoint K — GitHub sign-in decisions

2026-10-12. **Subsequently advanced by Metju's autonomous-implementation instruction.**

## Prepared

Read [GitHub sign-in and per-user data](../04-github-sign-in.md) and created
`github-sign-in/g0-decisions` from remote `master`, independently of the open
PR-connection-focus feature branch.

The supplied plan confirms GitHub App `tangle-metjuperry`, user authorization
tokens, state and S256 PKCE. Its slug and configuration keys are recorded in
[DECISIONS](../DECISIONS.md). No application settings were changed, no secrets
were inspected, no real sign-in was attempted, and no database was modified.

## Decisions needed before G1

The user initially was unavailable for checkpoint questions, then explicitly
requested autonomous implementation and reasonable decisions. Adopted:

| Decision | Recommendation to confirm |
| --- | --- |
| Sign-in allowlist | Start with login `metjuperry`; specify any additional logins or organizations. |
| Local development | Keep the development key as one fixed local user; Local remains default through checkpoint M. |
| New users' AI mode | Off until each user opts in. |
| Company key | Hide until a payer is explicitly approved. |

Confirm GitHub App readiness without sharing secret values:

- Public/installable by other accounts, or intentionally personal-only for now.
- Installed on metjuperry; TALXIS installation status and any required owner approval.
- Client ID and client secret configured in Host user-secrets or environment.
- Exact registered callback URL and intended Tangle bind address/port.
- Expiring user tokens enabled and permissions matching the supplied plan,
  including Members read if organization membership is used for admission.

## Prerequisites and next review

Issues #11–#14 and #16 are still open. The supplied plan requires them to be
merged before G1. Confirm whether the agent should implement those in separate
PRs or wait for them to be handled elsewhere.

There is no new runtime behavior to try at K. Prerequisite fixes are in PR #29;
G1 has its own dependent branch/PR and stops at checkpoint L
for a normal local-mode board review. GitHub sign-in itself belongs to G2 and
its first real sign-in remains checkpoint M.
