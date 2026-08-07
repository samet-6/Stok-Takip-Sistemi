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
  },
})
