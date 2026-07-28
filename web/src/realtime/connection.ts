import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
  type IRetryPolicy,
  type RetryContext,
} from '@microsoft/signalr'
import type { QueryClient } from '@tanstack/react-query'
import { hubTicket } from '../api/auth'
import { selectIsAuthenticated, useAuthStore } from '../stores/authStore'
import { registerRealtimeHandlers } from './events'

const HUB_URL = '/hubs/stok'

/**
 * Backoff steps in ms, the last one repeating forever.
 *
 * SignalR's built-in policy tries 0/2/10/30s and then gives up for good. In an app with
 * an 8-hour session that is wrong: a laptop asleep for five minutes would wake up with
 * the realtime layer permanently dead and no sign of it. We never stop trying, capped at
 * 60s so a long outage costs one request a minute instead of a stampede.
 */
const RETRY_DELAYS_MS = [0, 2_000, 5_000, 10_000, 30_000, 60_000]

function retryDelay(attempt: number): number {
  return RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)]
}

class ForeverRetryPolicy implements IRetryPolicy {
  nextRetryDelayInMilliseconds(context: RetryContext): number {
    return retryDelay(context.previousRetryCount)
  }
}

let connection: HubConnection | null = null
let startAttempt = 0
let startTimer: ReturnType<typeof setTimeout> | null = null

function build(queryClient: QueryClient): HubConnection {
  const built = new HubConnectionBuilder()
    .withUrl(HUB_URL, {
      // Invoked on every connection attempt, so each one gets a fresh 30s ticket.
      // This is also the "verify on connect" half of the architecture: while we were
      // offline the session may have been revoked, and then this POST 401s and the
      // axios interceptor logs the user out — no separate polling needed.
      accessTokenFactory: async () => (await hubTicket()).token,
    })
    .withAutomaticReconnect(new ForeverRetryPolicy())
    .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
    .build()

  // Handlers are attached once per connection object; automatic reconnect reuses the same
  // object, so they survive reconnects without being re-registered (and without stacking).
  registerRealtimeHandlers(built, queryClient)

  return built
}

async function tryStart(): Promise<void> {
  const current = connection
  if (!current || current.state !== HubConnectionState.Disconnected) return

  try {
    await current.start()
    startAttempt = 0
  } catch {
    // withAutomaticReconnect only covers connections that succeeded at least once — the
    // very first start is on us. Without this, logging in while the API is restarting
    // would leave the realtime layer dead until the user happens to refresh.
    startAttempt++
    if (connection === current) scheduleStart()
  }
}

function scheduleStart(): void {
  if (startTimer !== null) return

  startTimer = setTimeout(() => {
    startTimer = null
    void tryStart()
  }, retryDelay(startAttempt))
}

function connect(queryClient: QueryClient): void {
  if (connection) return

  connection = build(queryClient)
  startAttempt = 0
  void tryStart()
}

function disconnect(): void {
  if (startTimer !== null) {
    clearTimeout(startTimer)
    startTimer = null
  }

  const current = connection
  connection = null // cleared first so an in-flight tryStart sees it is stale
  void current?.stop()
}

/**
 * Binds the hub connection to session state. Called once from main.tsx.
 *
 * Deliberately outside React: a useEffect would be double-invoked under StrictMode and
 * churn the connection open/closed on every mount. The store subscription only fires on
 * transitions, so the already-logged-in case (page reload with a persisted token) is
 * covered by reading the current state up front.
 */
export function initRealtime(queryClient: QueryClient): void {
  const sync = (isAuthenticated: boolean) => {
    if (isAuthenticated) connect(queryClient)
    else disconnect()
  }

  sync(selectIsAuthenticated(useAuthStore.getState()))

  useAuthStore.subscribe((state, previous) => {
    const isAuthenticated = selectIsAuthenticated(state)
    if (isAuthenticated !== selectIsAuthenticated(previous)) sync(isAuthenticated)
  })
}
