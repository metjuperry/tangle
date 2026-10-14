output "resource_group" {
  value = azurerm_resource_group.tangle.name
}
output "blob_account" {
  value = azurerm_storage_account.data.name
}
output "database_container" {
  value = azurerm_storage_container.databases.name
}
output "key_vault_uri" {
  value = azurerm_key_vault.app.vault_uri
}
output "managed_identity_client_id" {
  value = azurerm_user_assigned_identity.app.client_id
}
output "container_environment_domain" {
  value = azurerm_container_app_environment.app.default_domain
}
output "container_app_fqdn" {
  value = var.deploy_application ? azurerm_container_app.app[0].ingress[0].fqdn : null
}
