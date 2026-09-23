/**
 * A random UUID (v4) for the Idempotency-Key header (D27).
 *
 * Built from crypto.getRandomValues rather than crypto.randomUUID: the latter exists only in
 * secure contexts (https, localhost), so the app opened over plain http on a LAN address would
 * have none. One code path that works everywhere beats a fallback that only ever runs in tests.
 */
export function newIdempotencyKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 0x0f) | 0x40 // version 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80 // RFC 4122 variant
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
