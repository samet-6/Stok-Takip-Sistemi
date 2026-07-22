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
import { purgeExpiredSession } from './stores/authStore'

// Drop a stale/expired token before the app hydrates from persisted storage.
purgeExpiredSession()

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { refetchOnWindowFocus: false },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
)
