import type { HubConnection } from '@microsoft/signalr'
import type { QueryClient } from '@tanstack/react-query'

/**
 * The ONLY place that maps a realtime event to the queries it invalidates.
 *
 * Kept in one module on purpose: if each page registered its own handler, adding a screen
 * would mean remembering to subscribe it, and a renamed query key would go stale in some
 * files but not others. Everything the signal touches lives under the three keys below.
 */

/** Server event names — must match `RealtimeEvents` on the backend, letter for letter. */
const ProductChanged = 'ProductChanged'
const NotificationsChanged = 'NotificationsChanged'

/**
 * Signals are batched, but on the LEADING edge: the first signal is applied immediately and
 * anything arriving in the next 300 ms is collected and applied once at the end.
 *
 * Trailing-only batching (wait 300 ms, then apply) was measured to make the common case —
 * a single movement by one other person — feel laggy for no reason, since it paid the full
 * window every time. Leading-edge keeps what the window was for: twenty movements in a row
 * cost two refetches instead of twenty, not one instead of twenty, and that difference is
 * worth nothing next to the latency it removes.
 *
 * (react-query only refetches queries that are actually mounted, so the blast radius is
 * already limited — this keeps the busy case from being silly on top of that.)
 */
const COALESCE_MS = 300

const pendingProductIds = new Set<number>()
/** Non-null while a window is open — also the flag that says "not the leading signal". */
let flushTimer: ReturnType<typeof setTimeout> | null = null

function apply(queryClient: QueryClient, productIds: Iterable<number>): void {
  // Prefix match: covers the list, the summary tiles, the picker and the catalog-detail
  // tabs in one call — every key that starts with 'products'.
  void queryClient.invalidateQueries({ queryKey: ['products'] })

  // Movement lists (Hesabım, Çalışan hareketleri, the catalog-detail tab) ride this same
  // signal instead of getting a MovementsChanged of their own. Movements are append-only, so
  // a movement list can only change when one is created — and every creation already fires
  // ProductChanged, which would leave a second event with no occasion of its own. It also
  // covers a case a movement-scoped event structurally could not see: the catalog-detail tab
  // filters movements by the product's supplier/category, so reassigning a product changes
  // that list without any movement being created.
  void queryClient.invalidateQueries({ queryKey: ['movements'] })

  // Detail queries are keyed per product, so only the ones that actually changed are hit.
  // This is what keeps "Stok: N" live in the picker and on the detail page.
  for (const id of productIds) {
    void queryClient.invalidateQueries({ queryKey: ['product', id] })
  }
}

function onProductChanged(queryClient: QueryClient, productId: number): void {
  if (flushTimer !== null) {
    // A window is already open — ride along and let it close.
    pendingProductIds.add(productId)
    return
  }

  // Leading edge: apply now, then hold a window so a burst behind this signal collapses.
  apply(queryClient, [productId])

  flushTimer = setTimeout(() => {
    flushTimer = null
    if (pendingProductIds.size === 0) return // nothing followed — the leading apply was it

    apply(queryClient, pendingProductIds)
    pendingProductIds.clear()
  }, COALESCE_MS)
}

export function registerRealtimeHandlers(
  connection: HubConnection,
  queryClient: QueryClient,
): void {
  connection.on(ProductChanged, (productId: number) => {
    onProductChanged(queryClient, productId)
  })

  // Delivered to admins only (server-side group), and carries nothing: the badge count comes
  // back with the refetch. No coalescing — a notification is a rare, deliberate event, not the
  // burst that the product window exists for.
  connection.on(NotificationsChanged, () => {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] })
  })

  // A dropped signal always means the connection was down, and a connection that was down
  // always ends in a reconnect — so a blanket invalidation here closes the gap without any
  // sequence numbers or gap detection. Cheap, because only mounted queries actually refetch.
  connection.onreconnected(() => {
    void queryClient.invalidateQueries()
  })
}
