import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import 'bootstrap/dist/css/bootstrap.min.css'
// Self-hosted Inter (woff2, same-origin → CSP/Docker safe). latin + latin-ext
// cover Turkish; weights 400/600/700 are all the visual system uses.
import '@fontsource/inter/latin-400.css'
import '@fontsource/inter/latin-ext-400.css'
import '@fontsource/inter/latin-600.css'
import '@fontsource/inter/latin-ext-600.css'
import '@fontsource/inter/latin-700.css'
import '@fontsource/inter/latin-ext-700.css'
import './styles/tokens.css'
import './styles/theme.css'
import { router } from './router'
import { ToastProvider } from './components/ToastProvider'
import { initRealtime } from './realtime/connection'
import { purgeExpiredSession } from './stores/authStore'

// Drop a stale/expired token before the app hydrates from persisted storage.
purgeExpiredSession()

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // These two only make sense together, and they are global on purpose so a new query
      // can't silently opt out of live behaviour by forgetting to set them.
      //
      // refetchOnWindowFocus covers what push cannot: a tab that was hidden while the
      // browser throttled its timers. staleTime is what makes it affordable — without it
      // (default 0) every single tab switch would refetch everything on screen.
      //
      // Realtime is unaffected by staleTime: invalidateQueries ignores it and refetches
      // mounted queries immediately, so a push is still instant.
      refetchOnWindowFocus: true,
      staleTime: 15_000,
    },
  },
})

// Open/close the hub with the session — after the purge above, so an expired token never
// gets as far as asking for a hub ticket. The client is passed in rather than imported by
// the realtime module: one QueryClient, owned here, no second instance to drift from.
initRealtime(queryClient)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
)
