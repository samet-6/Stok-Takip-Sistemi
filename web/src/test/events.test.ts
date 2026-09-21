import type { HubConnection } from '@microsoft/signalr'
import type { QueryClient } from '@tanstack/react-query'
import { registerRealtimeHandlers } from '../realtime/events'

const COALESCE_MS = 300

type Handler = (...args: never[]) => void

/** Captures the handlers the module registers, so tests can fire server events by hand. */
function harness() {
  const handlers = new Map<string, Handler>()
  let reconnected: Handler = () => {}

  const connection = {
    on: (name: string, handler: Handler) => handlers.set(name, handler),
    onreconnected: (handler: Handler) => {
      reconnected = handler
    },
  } as unknown as HubConnection

  const invalidateQueries = vi.fn()
  const queryClient = { invalidateQueries } as unknown as QueryClient

  registerRealtimeHandlers(connection, queryClient)

  return {
    invalidateQueries,
    productChanged: (id: number) => handlers.get('ProductChanged')?.(id as never),
    notificationsChanged: () => handlers.get('NotificationsChanged')?.(),
    reconnect: () => reconnected(),
    keys: () => invalidateQueries.mock.calls.map(([arg]) => arg?.queryKey),
  }
}

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  // Drain the coalescing window: the module keeps its timer and pending ids in module scope,
  // so an unfinished window would leak into the next test.
  vi.runOnlyPendingTimers()
  vi.useRealTimers()
})

describe('ProductChanged', () => {
  // Leading edge: the common case — one other person adds one movement — must not pay the
  // 300 ms window before the screen updates.
  it('ilk sinyali beklemeden uyguluyor', () => {
    const h = harness()

    h.productChanged(7)

    expect(h.keys()).toEqual([['products'], ['movements'], ['product', 7]])
  })

  it('pencere içindeki ikinci sinyali hemen uygulamıyor', () => {
    const h = harness()

    h.productChanged(7)
    h.invalidateQueries.mockClear()
    h.productChanged(8)

    expect(h.invalidateQueries).not.toHaveBeenCalled()
  })

  it('pencere kapanınca biriken sinyalleri tek turda uyguluyor', () => {
    const h = harness()

    h.productChanged(7)
    h.productChanged(8)
    h.productChanged(9)
    h.invalidateQueries.mockClear()

    vi.advanceTimersByTime(COALESCE_MS)

    expect(h.keys()).toEqual([['products'], ['movements'], ['product', 8], ['product', 9]])
  })

  it('tek sinyalden sonra pencere boş kapanıyor — ikinci tur yok', () => {
    const h = harness()

    h.productChanged(7)
    h.invalidateQueries.mockClear()

    vi.advanceTimersByTime(COALESCE_MS)

    expect(h.invalidateQueries).not.toHaveBeenCalled()
  })

  it('pencere kapandıktan sonra gelen sinyal yeniden öncü oluyor', () => {
    const h = harness()

    h.productChanged(7)
    vi.advanceTimersByTime(COALESCE_MS)
    h.invalidateQueries.mockClear()

    h.productChanged(8)

    expect(h.keys()).toEqual([['products'], ['movements'], ['product', 8]])
  })
})

describe('NotificationsChanged', () => {
  it('yalnız bildirimleri tazeliyor — toplu değil', () => {
    const h = harness()

    h.notificationsChanged()

    expect(h.keys()).toEqual([['notifications']])
  })
})

describe('onreconnected', () => {
  // A dropped signal always means the connection was down, so the reconnect closes the gap
  // with a blanket invalidation — no argument at all.
  it('her şeyi tazeliyor', () => {
    const h = harness()

    h.reconnect()

    expect(h.invalidateQueries).toHaveBeenCalledWith()
  })
})
