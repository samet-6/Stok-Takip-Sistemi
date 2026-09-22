// `vitest/config` re-exports Vite's own defineConfig and adds the `test` key below, so the
// dev/build config and the test config stay in ONE file and cannot drift apart.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Explicit IPv4 loopback. Left to its default, vite bound only to ::1 on this machine
    // while `localhost` resolves to both ::1 and 127.0.0.1 — the browser tried IPv4 first,
    // got no answer, and waited out Windows' ~2s SYN retry before falling back to IPv6.
    // Result: the first request on every fresh connection cost 2 seconds and looked like an
    // application problem (measured 2035ms → 281ms → 8ms as keep-alive kicked in).
    // 127.0.0.1 rather than true/'::' on purpose: this keeps the dev server off the LAN.
    host: '127.0.0.1',
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5000',
      // ws: true — the hub handshake is an HTTP Upgrade. Without it the negotiate POST
      // would be forwarded and the WebSocket that follows would be dropped.
      '/hubs': { target: 'http://localhost:5000', ws: true },
    },
  },
  test: {
    // Unit/component layer. End-to-end lives in ../e2e (Playwright) and is not run by vitest.
    environment: 'jsdom',
    // Testing Library registers its automatic cleanup through the global afterEach, which
    // only exists when globals are on. Without this, state leaks between test files.
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
    coverage: {
      provider: 'v8',
      // lcov is what SonarQube reads (sonar.javascript.lcov.reportPaths); text-summary keeps
      // the number visible in the terminal without printing a per-file table.
      reporter: ['text-summary', 'lcov'],
      reportsDirectory: './coverage',
      // `include` is what makes untested files count as 0% instead of vanishing from the
      // denominator — a coverage number that ignores untested files flatters itself. (The
      // old `all: true` switch is gone in vitest 4; reporting every included file is the
      // default now, so naming the include glob is the whole job.)
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        'src/test/**',
        'src/**/*.d.ts',
        // Composition roots: wiring with no branches of its own, and covering them would
        // mean booting the whole app in jsdom for no assertion.
        'src/main.tsx',
      ],
    },
  },
})
