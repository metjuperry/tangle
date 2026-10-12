import { defineConfig } from '@playwright/test'

export default defineConfig({
  workers: 1,
  testDir: '../tests/oauth-e2e',
  use: { baseURL: 'http://127.0.0.1:5203' },
  webServer: {
    command: 'dotnet run --project ../tests/Tangle.Web.Tests --no-launch-profile',
    url: 'http://127.0.0.1:5203/healthz',
    env: { ASPNETCORE_ENVIRONMENT: 'Development', TANGLE_FAKE_OAUTH: 'true' },
    reuseExistingServer: false,
  },
})
