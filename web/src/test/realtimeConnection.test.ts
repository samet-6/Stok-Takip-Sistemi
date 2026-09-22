import type { QueryClient } from '@tanstack/react-query'

// The hub client is faked down to the three things this module drives: the builder chain, the
// start/stop pair, and the handler registration events.ts performs on whatever is built.
const mocks = vi.hoisted(() => {
  const connections: {
    state: string
    start: ReturnType<typeof vi.fn>
    stop: ReturnType<typeof vi.fn>
  }[] = []

  function createConnection() {
    const connection = {
      state: 'Disconnected',
      start: vi.fn(async () => {
        connection.state = 'Connected'
      }),
      stop: vi.fn(async () => {
        connection.state = 'Disconnected'
      }),
      on: vi.fn(),
      onreconnected: vi.fn(),
    }

    connections.push(connection)
    return connection
  }

  return { connections, createConnection }
})

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return mocks.createConnection()
    }
  },
  HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
  LogLevel: { Information: 1, Warning: 2 },
}))

vi.mock('../api/auth', () => ({
  hubTicket: vi.fn(async () => ({ token: 'test-ticket', expiresAt: '' })),
}))

const queryClient = {} as QueryClient

/**
 * connection.ts keeps its connection in module scope, so every case needs a fresh copy. The
 * store is re-imported in the same reset registry — importing it once at the top would hand
 * the test a different instance than the one the module under test subscribes to.
 */
async function load() {
  vi.resetModules()
  mocks.connections.length = 0

  const { useAuthStore } = await import('../stores/authStore')
  const { initRealtime } = await import('../realtime/connection')

  useAuthStore.setState({ token: null, expiresAt: null, user: null })

  return { useAuthStore, initRealtime }
}

describe('initRealtime', () => {
  it('çıkışlı durumda açılırsa bağlantı kurmuyor', async () => {
    const { initRealtime } = await load()

    initRealtime(queryClient)

    expect(mocks.connections).toHaveLength(0)
  })

  // The page-reload case: the token is already in the persisted store when main.tsx runs, and
  // the store subscription only fires on transitions — so nothing would ever start it.
  it('zaten girişliyken açılırsa bağlantıyı hemen kuruyor', async () => {
    const { useAuthStore, initRealtime } = await load()
    useAuthStore.setState({ token: 'oturum-token' })

    initRealtime(queryClient)

    expect(mocks.connections).toHaveLength(1)
    expect(mocks.connections[0].start).toHaveBeenCalledTimes(1)
  })

  it('giriş bağlantıyı kuruyor, çıkış onu durduruyor', async () => {
    const { useAuthStore, initRealtime } = await load()
    initRealtime(queryClient)

    useAuthStore.setState({ token: 'oturum-token' })
    expect(mocks.connections).toHaveLength(1)
    expect(mocks.connections[0].start).toHaveBeenCalledTimes(1)

    useAuthStore.setState({ token: null })
    expect(mocks.connections[0].stop).toHaveBeenCalledTimes(1)
  })

  // Only transitions matter. A store write that leaves the session alone (the user object
  // arriving, a query cache update) must not churn the socket open and closed.
  it('oturumu değiştirmeyen store yazımlarında bağlantıya dokunmuyor', async () => {
    const { useAuthStore, initRealtime } = await load()
    useAuthStore.setState({ token: 'oturum-token' })
    initRealtime(queryClient)

    useAuthStore.setState({ expiresAt: '2026-12-31T00:00:00Z' })

    expect(mocks.connections).toHaveLength(1)
    expect(mocks.connections[0].stop).not.toHaveBeenCalled()
  })
})
