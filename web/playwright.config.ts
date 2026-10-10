import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: '../tests/web-e2e',
  use: { baseURL: 'http://127.0.0.1:5203', viewport: { width: 1640, height: 1200 } },
  webServer: {
    command: 'dotnet run --project ../tests/Tangle.Web.Tests --no-launch-profile',
    url: 'http://127.0.0.1:5203/healthz',
    env: { ASPNETCORE_ENVIRONMENT: 'Development' },
    reuseExistingServer: false,
  },
})
