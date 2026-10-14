# Tangle infrastructure (prepare now, deploy later)

This directory defines Azure resources but **nothing has been deployed**.
It uses the current GitHub App sign-in/per-user design (`docs/04-github-sign-in.md`),
not the superseded Container Apps principal-only login.

| Resource | Purpose |
| --- | --- |
| Resource group | Application resources and budget scope |
| Standard LRS Storage, two private Blob containers | SQLite replicas/lease and Data Protection key documents |
| User-assigned managed identity and scoped RBAC | Blob access, Key Vault secret access and wrapping-key use without storage keys |
| RBAC Key Vault and RSA wrapping key | GitHub client secret reference and future persistent Data Protection |
| Container Apps environment, no Log Analytics | Consumption hosting and live logs without a log-ingestion workspace |
| Optional Container App | Public HTTPS, Single revisions, 0–1 replicas, 0.25 CPU/0.5 GiB, ghcr.io digest-pinned image |
| Monthly resource-group budget | Email alerts at 80% and 100%; **not a spending cap** |

No Azure SQL, ACR, scheduled jobs or automatic deployment trigger is added.
The manually dispatched [CD workflow](../docs/deploy.md) plans first and gates
apply behind required production reviewers.
Key Vault, Storage and compute can still incur charges; free grants are shared
with the subscription, not guaranteed to cover this app.

## Safe local checks

```sh
terraform init -backend=false
terraform fmt -check
terraform validate
terraform test
```

The included Terraform test uses a **mock provider** and only plans. These checks
do not create Azure resources. Keep `.terraform.lock.hcl` committed.

## Later provisioning

Copy `terraform.tfvars.example` to `terraform.tfvars` and fill subscription,
tenant, Terraform principal **object ID**, globally unique suffix, current-month
budget date and notification emails. No secret values belong in this file.
Authenticate using Azure CLI or an explicitly configured OIDC principal.

Register `Microsoft.App`, `Microsoft.Storage`, `Microsoft.KeyVault`,
`Microsoft.ManagedIdentity`, and `Microsoft.Consumption` with the subscription
before planning. Automatic resource-provider registration is disabled.
The provisioning principal needs resource creation, role-assignment and budget
permissions. Key Vault RBAC may take time to propagate before key creation.

Keep `deploy_application=false` for the infrastructure-only first deployment.
Review the actual plan and costs yourself before any apply. No apply command
has been run by the agent. Terraform state may contain sensitive metadata;
keep it private and configure a secure backend before shared use.

After provisioning, populate the GitHub client secret in Key Vault using your
own Secrets Officer/Admin permissions (Terraform is granted Crypto Officer to
create the wrapping key, **not** Secrets Officer). Supply only its secret URI.
The app's identity reads that URI through a Key Vault reference, so Terraform
never receives the client secret value.

## Application gate — do not skip

The current repository does **not** have a production container entry point
implementing all of the following. `deploy_application=true` requires explicit
`cloud_runtime_verified=true` as a guard, not as an implementation:

1. Acquire/renew a Blob lease on `writer.lock` **before** restoring or opening
   SQLite. Keep it across deployment overlap; stop writes if renewal fails.
2. Restore and replicate `tangle.db`, `data/control.db` and **every**
   `data/users/<id>.db`, including newly created and deleted files. Verify crash
   recovery and account deletion do not resurrect old databases.
3. Persist Data Protection keys in `protection-keys`, wrapped with the supplied
   Key Vault key, and unwrap them before serving auth. The current local
   filesystem key repository is not enough on ephemeral container disk.
4. Drain/flush changes before releasing the lease at shutdown. Max replicas 1
   and Single revisions alone do **not** prevent overlapping writers.

`TANGLE_BLOB_*` and `TANGLE_KEY_*` are the intended **future entry-point contract**;
the current app does not consume those settings. `/data` and `/keys` are EmptyDir
volumes, safe only after that restore/replication contract is implemented.
No claim of durable deployment is made for the current local image.

Then supply a public ghcr.io image digest, the GitHub App client ID/secret URI
and the exact HTTPS `public_origin`. Register
`<public_origin>/auth/github/callback` yourself on `tangle-metjuperry`.
Use the environment's default domain plus the app name for the generated ACA
hostname, or arrange DNS/custom-domain TLS binding separately. This configuration
does not provision a DNS zone or custom hostname binding.

AI and Apply remain off in the container configuration. Per-user AI budgets
and permissions still apply if those switches are explicitly enabled later.
No personal development key or per-org GitHub token is injected.

Budgets exclude external provider charges and may not cover platform-managed
resources outside the application's resource group. Soft delete/purge
protection intentionally delay complete resource cleanup.
