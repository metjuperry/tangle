# Continuous deployment

`.github/workflows/cd.yml` is a **manual** deployment workflow. Pushes and pull
requests run CI only; they never apply infrastructure or deploy the app.
This workflow has not been dispatched and no Azure resources have been created.

## What CD does

1. Accepts only the default `master` branch and requires successful CI on that
   exact commit.
2. Signs in to Azure using OIDC, initializes existing remote Terraform state,
   validates and creates a saved plan.
3. Defaults to `operation=plan`; no apply job runs in that mode.
4. For `operation=apply`, waits on the `production` environment, verifies that
   required reviewers are configured, and applies **the saved plan from this
   workflow run**. The apply identity/backend must match the reviewed plan.
5. If the app was included, checks `/healthz` after deployment.

Deployment runs are serialized. Cancellation never interrupts an older apply
automatically. Plans are private workflow artifacts retained for one day; they
can contain sensitive state metadata, so restrict repository access. Re-run
planning rather than approving an expired artifact.

## One-time setup (you perform it)

Create GitHub environments **`azure-plan`** and **`production`**. Restrict
`production` to `master`, require reviewers, and prevent self-review/bypass where
your GitHub plan allows it. The workflow refuses to apply without a
required-reviewers protection rule.

Create/configure an Azure Entra application or managed identity for GitHub OIDC.
Its federated subjects must match:

```text
repo:metjuperry/tangle:environment:azure-plan
repo:metjuperry/tangle:environment:production
```

Issuer: `https://token.actions.githubusercontent.com`

Audience: `api://AzureADTokenExchange`

Give this principal the permissions described in `infra/README.md` for resource
creation, role assignments and budgets. Prefer resource-group scope where
possible; initial resource-group creation may require broader setup permissions.
No client secret is used or stored in GitHub.

Create a **separate existing Terraform-state Storage account/container** outside
the resources managed by this configuration. Enable Azure AD/RBAC access and
give the OIDC principal Storage Blob Data Contributor on its state container.
Do not put state into the application's replica container; state must survive
app resource destruction.

Set these as repository Actions **variables**, with identical values available
to both environments (avoid environment overrides):

| Variable | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | OIDC application's client ID |
| `AZURE_TENANT_ID` | Tenant UUID |
| `AZURE_SUBSCRIPTION_ID` | Subscription UUID |
| `AZURE_PROVISIONER_OBJECT_ID` | Service principal object ID, not client ID |
| `TF_STATE_ACCOUNT` | Existing backend Storage account |
| `TF_STATE_CONTAINER` | Existing backend Blob container |
| `TF_STATE_RESOURCE_GROUP` | Backend account resource group |
| `TANGLE_AZURE_NAME_SUFFIX` | Globally unique 6–12 lowercase letters/digits |
| `TANGLE_AZURE_LOCATION` | Optional; defaults to `westeurope` |
| `TANGLE_BUDGET_EMAILS_JSON` | JSON list, e.g. `["you@example.com"]` |
| `TANGLE_BUDGET_START_DATE` | First day of deployment month, UTC |
| `TANGLE_MONTHLY_BUDGET` | Optional; defaults to 10 in subscription currency |

The backend key is `tangle-production.tfstate`. If migrating an earlier local
state, do that explicitly and carefully before using CD; do not initialize an
empty backend over already-managed resources.

## Plan, then deploy

After the workflow is merged to `master`, open Actions → CD → Run workflow.
Start with **plan** and **include app=false**. Read the plan, scopes and costs.
When ready, run **apply** with the same selection, inspect that run's new plan
and approve its protected production job.

Budget alerts are notifications, not a spending cap. Planning needs Azure read
access but does not create resources; applying can incur costs.

## Container image and durable runtime

This CD workflow deploys an **existing verified image by digest**, rather than
building an unsafe local-only image. The repository still lacks the cloud
entry point/Blob lease/restore/replication/persistent wrapped-key runtime
described in `infra/README.md`. CI currently tests the application, not that
missing cloud runtime. Therefore keep **include app=false** today.

When that runtime is implemented and verified, publish a public image to
`ghcr.io` through a separately reviewed build/publish workflow. Then configure:

| Variable/input | Value |
| --- | --- |
| `TANGLE_CLOUD_RUNTIME_VERIFIED` | `true` only after durable runtime testing |
| `TANGLE_PUBLIC_ORIGIN` | Exact configured HTTPS origin |
| `TANGLE_GITHUB_CLIENT_ID` | GitHub App client ID |
| `TANGLE_GITHUB_SECRET_URI` | Existing Key Vault secret URI, **not its value** |
| Dispatch `container_image` | `ghcr.io/...@sha256:<digest>` |

Register the exact GitHub App callback and arrange DNS/TLS bindings as described
in `infra/README.md`. No GitHub client secret, Anthropic key or storage-account
key is passed through workflow inputs or Terraform variables.

AI and GitHub Apply remain disabled by the Terraform container configuration.
Do not enable either without the corresponding permissions/funding approval.
This is deployment pipeline preparation, not completion of M9's cloud runtime.
