mock_provider "azurerm" {}

variables {
  subscription_id       = "00000000-0000-0000-0000-000000000001"
  tenant_id             = "00000000-0000-0000-0000-000000000002"
  provisioner_object_id = "00000000-0000-0000-0000-000000000003"
  name_suffix           = "testing123"
  budget_start_date     = "2026-10-01T00:00:00Z"
  budget_emails         = ["test@example.com"]
}

run "infrastructure_only_by_default" {
  command = plan
  assert {
    condition     = length(azurerm_container_app.app) == 0
    error_message = "The unverified app image must not be deployed by default."
  }

  assert {
    condition     = azurerm_storage_account.data.shared_access_key_enabled == false && azurerm_storage_container.databases.container_access_type == "private"
    error_message = "Storage must use managed-identity/RBAC access and private containers."
  }
  assert {
    condition     = azurerm_container_app_environment.app.log_analytics_workspace_id == null
    error_message = "Do not provision a paid log ingestion workspace."
  }
}
run "reject_unverified_app" {
  command = plan
  variables {
    deploy_application       = true
    cloud_runtime_verified   = false
    public_origin            = "https://tangle.example.com"
    container_image          = "ghcr.io/example/tangle@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    github_client_id         = "fake-client"
    github_client_secret_uri = "https://tangle-testing123.vault.azure.net/secrets/github-client-secret"
  }
  expect_failures = [azurerm_container_app.app]
}
