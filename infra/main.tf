terraform {
  backend "azurerm" {}
  required_version = ">= 1.9, < 2.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}

provider "azurerm" {
  features {}
  subscription_id                 = var.subscription_id
  tenant_id                       = var.tenant_id
  resource_provider_registrations = "none"
  storage_use_azuread             = true
}

locals {
  name = "tangle-${var.name_suffix}"
  app_environment = merge({
    ASPNETCORE_ENVIRONMENT    = "Production"
    Tangle__Address           = "http://0.0.0.0:8080"
    Tangle__DatabasePath      = "/data/tangle.db"
    Auth__Mode                = "GitHub"
    Auth__AdminLogin          = var.admin_login
    Auth__KeyDirectory        = "/keys"
    GitHub__App__ClientId     = var.github_client_id
    GitHub__App__CallbackUrl  = "${var.public_origin}/auth/github/callback"
    Tangle__AllowedHosts__0   = trimprefix(var.public_origin, "https://")
    Tangle__AllowedOrigins__0 = var.public_origin
    Ai__Enabled               = "false"
    Ai__CompanyEnabled        = "false"
    Ai__JevEnabled            = "false"
    Apply__Enabled            = "false"
    AZURE_CLIENT_ID           = azurerm_user_assigned_identity.app.client_id
    TANGLE_BLOB_ACCOUNT       = azurerm_storage_account.data.name
    TANGLE_BLOB_CONTAINER     = azurerm_storage_container.databases.name
    TANGLE_BLOB_LEASE_NAME    = "writer.lock"
    TANGLE_KEY_BLOB_CONTAINER = azurerm_storage_container.keys.name
    TANGLE_KEY_VAULT_KEY_ID   = azurerm_key_vault_key.protection.versionless_id
    }, {
    for index, login in var.allowed_logins : "Auth__AllowedLogins__${index}" => login
  })
}

resource "azurerm_resource_group" "tangle" {
  name     = local.name
  location = var.location
  tags     = var.tags
}

resource "azurerm_storage_account" "data" {
  name                            = "tangle${var.name_suffix}"
  resource_group_name             = azurerm_resource_group.tangle.name
  location                        = azurerm_resource_group.tangle.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  allow_nested_items_to_be_public = false
  public_network_access_enabled   = true
  tags                            = var.tags
  blob_properties {
    delete_retention_policy {
      days = 7
    }
    container_delete_retention_policy {
      days = 7
    }
  }
}

resource "azurerm_storage_container" "databases" {
  name                  = "databases"
  storage_account_id    = azurerm_storage_account.data.id
  container_access_type = "private"
}

resource "azurerm_storage_container" "keys" {
  name                  = "protection-keys"
  storage_account_id    = azurerm_storage_account.data.id
  container_access_type = "private"
}

resource "azurerm_user_assigned_identity" "app" {
  name                = "${local.name}-identity"
  resource_group_name = azurerm_resource_group.tangle.name
  location            = azurerm_resource_group.tangle.location
  tags                = var.tags
}

resource "azurerm_role_assignment" "blob" {
  for_each             = { databases = azurerm_storage_container.databases.id, keys = azurerm_storage_container.keys.id }
  scope                = each.value
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_key_vault" "app" {
  name                       = "tangle-${var.name_suffix}"
  location                   = azurerm_resource_group.tangle.location
  resource_group_name        = azurerm_resource_group.tangle.name
  tenant_id                  = var.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = true
  soft_delete_retention_days = 7
  tags                       = var.tags
}

resource "azurerm_role_assignment" "provisioner_keys" {
  scope                = azurerm_key_vault.app.id
  role_definition_name = "Key Vault Crypto Officer"
  principal_id         = var.provisioner_object_id
}

resource "azurerm_key_vault_key" "protection" {
  name         = "data-protection"
  key_vault_id = azurerm_key_vault.app.id
  key_type     = "RSA"
  key_size     = 2048
  key_opts     = ["wrapKey", "unwrapKey"]
  depends_on   = [azurerm_role_assignment.provisioner_keys]
}

resource "azurerm_role_assignment" "unwrap" {
  scope                = azurerm_key_vault_key.protection.resource_id
  role_definition_name = "Key Vault Crypto Service Encryption User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_role_assignment" "secrets" {
  scope                = azurerm_key_vault.app.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_container_app_environment" "app" {
  name                = "${local.name}-environment"
  location            = azurerm_resource_group.tangle.location
  resource_group_name = azurerm_resource_group.tangle.name
  tags                = var.tags
  # No Log Analytics workspace or diagnostic sink: live log streaming only.
}

resource "azurerm_container_app" "app" {
  count                        = var.deploy_application ? 1 : 0
  name                         = local.name
  container_app_environment_id = azurerm_container_app_environment.app.id
  resource_group_name          = azurerm_resource_group.tangle.name
  revision_mode                = "Single"
  max_inactive_revisions       = 1
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }
  secret {
    name                = "github-client-secret"
    identity            = azurerm_user_assigned_identity.app.id
    key_vault_secret_id = var.github_client_secret_uri
  }
  ingress {
    external_enabled           = true
    allow_insecure_connections = false
    target_port                = 8080
    transport                  = "http"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }
  template {
    min_replicas                     = 0
    max_replicas                     = 1
    termination_grace_period_seconds = 60
    http_scale_rule {
      name                = "http"
      concurrent_requests = "10"
    }
    volume {
      name         = "data"
      storage_type = "EmptyDir"
    }
    volume {
      name         = "keys"
      storage_type = "EmptyDir"
    }
    container {
      name   = "tangle"
      image  = var.container_image
      cpu    = 0.25
      memory = "0.5Gi"
      volume_mounts {
        name = "data"
        path = "/data"
      }
      volume_mounts {
        name = "keys"
        path = "/keys"
      }
      dynamic "env" {
        for_each = local.app_environment
        content {
          name  = env.key
          value = env.value
        }
      }
      env {
        name        = "GitHub__App__ClientSecret"
        secret_name = "github-client-secret"
      }
      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/healthz"
        header {
          name  = "Host"
          value = trimprefix(var.public_origin, "https://")
        }
      }
      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/healthz"
        header {
          name  = "Host"
          value = trimprefix(var.public_origin, "https://")
        }
      }
    }
  }
  lifecycle {
    precondition {
      condition     = var.cloud_runtime_verified
      error_message = "Do not deploy the current local image. Verify Blob lease, restore/replication for every SQLite file, and persistent Key Vault-wrapped Data Protection keys first."
    }
    precondition {
      condition     = contains(var.allowed_logins, var.admin_login)
      error_message = "Keep the administrator login on the explicit sign-in allowlist."
    }
    precondition {
      condition     = var.public_origin != "" && var.github_client_id != "" && var.github_client_secret_uri != "" && var.container_image != ""
      error_message = "App deployment requires a public HTTPS origin, GitHub App client ID/Key Vault secret URI and a verified image."
    }
  }
  depends_on = [azurerm_role_assignment.blob, azurerm_role_assignment.secrets, azurerm_role_assignment.unwrap]
}

resource "azurerm_consumption_budget_resource_group" "app" {
  name              = "${local.name}-budget"
  resource_group_id = azurerm_resource_group.tangle.id
  amount            = var.monthly_budget
  time_grain        = "Monthly"
  time_period {
    start_date = var.budget_start_date
  }
  notification {
    enabled        = true
    threshold      = 80
    operator       = "GreaterThanOrEqualTo"
    contact_emails = var.budget_emails
  }
  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThanOrEqualTo"
    contact_emails = var.budget_emails
  }
}
