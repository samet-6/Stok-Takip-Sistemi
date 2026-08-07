import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end configuration.
 *
 * The only thing that changes between environments is the base address, because BOTH
 * environments serve the API under the same origin at /api: the Vite dev server proxies
 * /api → :5000 (web/vite.config.ts), and in Docker nginx proxies it to the api container
 * (docker-compose.yml — the API is never published to the host). So a single baseURL
 * switch covers dev, Docker-from-the-host, and Docker-internal.
 *
 *   compose (default, and how the project is graded) : http://web        (container network)
 *   docker from the host                             : http://localhost:3000
 *   vite dev server                                  : http://127.0.0.1:5173
 *
 * There is deliberately no `webServer` block: bringing the stack up is `docker compose`'s
 * job, and having the test runner shell out to docker in the background turns a missing
 * container into an unreadable timeout.
 */
export default defineConfig({
  testDir: './tests',

  // A failing assertion must not be reported as a pass because someone left a `.only`
  // behind, and CI must not silently accept a suite with nothing in it.
  forbidOnly: !!process.env.CI,

  // One worker on purpose. Every test hits ONE shared, seeded PostgreSQL — there is no
  // per-test isolation yet (the backend suite learned this the hard way: parallel tests
  // against shared state produce both false greens and false reds). Revisit together with
  // per-test data isolation, not before.
  workers: 1,
  fullyParallel: false,

  retries: process.env.CI ? 1 : 0,

  reporter: [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    // Evidence for failures only — a green run leaves nothing behind.
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'off',
    locale: 'tr-TR',
    timezoneId: 'Europe/Istanbul',
  },

  // Chromium only. The app is an internal stock-tracking tool with one deployment target;
  // three browser downloads (~500 MB) would buy coverage nobody consumes.
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
