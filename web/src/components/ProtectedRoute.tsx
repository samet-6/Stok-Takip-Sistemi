import { Navigate, Outlet } from 'react-router'
import { useAuthStore, selectIsAuthenticated } from '../stores/authStore'

/** Blocks unauthenticated users, sending them to /login. */
export function ProtectedRoute() {
  const isAuthenticated = useAuthStore(selectIsAuthenticated)
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }
  return <Outlet />
}
