variable "subscription_id" {
  type        = string
  description = "Azure subscription to provision later. Authenticate separately using Azure CLI or OIDC."
}
variable "tenant_id" {
  type = string
}
variable "provisioner_object_id" {
  type        = string
  description = "Object ID (not client ID) of the principal running Terraform; used to create the Key Vault wrapping key."
}
variable "name_suffix" {
  type        = string
  description = "Globally unique lowercase suffix for Storage/Key Vault names."
  validation {
    condition     = can(regex("^[a-z0-9]{6,12}$", var.name_suffix))
    error_message = "Use 6-12 lowercase letters/digits."
  }
}
variable "location" {
  type    = string
  default = "westeurope"
}
variable "tags" {
  type    = map(string)
  default = { application = "tangle", managed_by = "terraform" }
}
variable "deploy_application" {
  type        = bool
  default     = false
  description = "Keep false until cloud-runtime requirements are implemented; infrastructure only by default."
}
variable "cloud_runtime_verified" {
  type        = bool
  default     = false
  description = "Explicitly acknowledge a tested Blob lease/restore/replication and persistent encrypted Data Protection implementation."
}
variable "container_image" {
  type        = string
  default     = ""
  description = "Public ghcr.io image pinned to a digest. No registry/password resources are provisioned."
  validation {
    condition     = var.container_image == "" || can(regex("^ghcr\\.io/.+@sha256:[a-f0-9]{64}$", var.container_image))
    error_message = "Use a public ghcr.io image pinned with @sha256:<64 lowercase hex digits>."
  }
}
variable "public_origin" {
  type        = string
  default     = ""
  description = "Exact externally reachable HTTPS origin, no trailing slash. DNS/TLS hostname binding must exist before use."
  validation {
    condition     = var.public_origin == "" || can(regex("^https://[a-zA-Z0-9.-]+$", var.public_origin))
    error_message = "Supply an HTTPS hostname origin without a path or trailing slash."
  }
}
variable "github_client_id" {
  type    = string
  default = ""
}
variable "github_client_secret_uri" {
  type        = string
  default     = ""
  description = "Existing Key Vault secret URI, NOT the secret value. Populate it manually after infrastructure exists."
  validation {
    condition     = var.github_client_secret_uri == "" || can(regex("^https://[a-zA-Z0-9-]+\\.vault\\.azure\\.net/secrets/[^/]+(/[^/]+)?$", var.github_client_secret_uri))
    error_message = "Supply a Key Vault secret URI, never a client secret value."
  }
}
variable "allowed_logins" {
  type    = list(string)
  default = ["metjuperry"]
  validation {
    condition     = length(var.allowed_logins) > 0 && alltrue([for login in var.allowed_logins : can(regex("^[A-Za-z0-9][A-Za-z0-9-]{0,38}$", login))])
    error_message = "Provide explicit valid GitHub logins."
  }
}
variable "admin_login" {
  type    = string
  default = "metjuperry"
}
variable "monthly_budget" {
  type        = number
  default     = 10
  description = "Monthly resource-group budget in subscription billing currency. Alerts do not stop spending."
  validation {
    condition     = var.monthly_budget > 0
    error_message = "Budget must be positive."
  }
}
variable "budget_start_date" {
  type        = string
  description = "First day of deployment month, e.g. 2026-10-01T00:00:00Z."
  validation {
    condition     = can(regex("^\\d{4}-\\d{2}-01T00:00:00Z$", var.budget_start_date))
    error_message = "Use the first day of the month at midnight UTC."
  }
}
variable "budget_emails" {
  type = list(string)
  validation {
    condition     = length(var.budget_emails) > 0 && alltrue([for email in var.budget_emails : can(regex("^[^@ ]+@[^@ ]+\\.[^@ ]+$", email))])
    error_message = "Supply at least one budget notification email."
  }
}
