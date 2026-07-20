import { Navigate, Outlet } from 'react-router'
import { useAuthStore, selectIsAuthenticated } from '../stores/authStore'

/** Login/register pages: an already-authenticated user is sent home. */
export function AnonymousRoute() {
  const isAuthenticated = useAuthStore(selectIsAuthenticated)
  if (isAuthenticated) {
    return <Navigate to="/" replace />
  }
  return <Outlet />
}
