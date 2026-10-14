# Deployment deferred

Metju explicitly deferred M9 Azure deployment on 2026-10-10 in favor of local
polish. Terraform preparation was requested on 2026-10-14 and is available in
`infra/`, with app deployment gated pending the cloud runtime. No Azure resources,
Terraform apply, cloud secrets or deployment costs have been created.
Resume M9 from `03-implementation-order.md` when hosting is
needed, including SQLite single-writer lease, backup/restore and sign-in review.
