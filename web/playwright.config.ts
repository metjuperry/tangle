import { defineConfig } from '@playwright/test'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { randomUUID } from 'node:crypto'

export default defineConfig({
  testDir: '../tests/web-e2e',
  use: { baseURL: 'http://127.0.0.1:5203' },
  webServer: {
    command: 'dotnet run --project ../src/Tangle.Host --no-launch-profile',
    url: 'http://127.0.0.1:5203/healthz',
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      Tangle__Address: 'http://127.0.0.1:5203',
      Tangle__DevelopmentKey: 'tangle-browser-test-key-not-a-secret',
      Tangle__DatabasePath: join(tmpdir(), `tangle-e2e-${randomUUID()}`, 'test.db'),
    },
    reuseExistingServer: false,
  },
})
