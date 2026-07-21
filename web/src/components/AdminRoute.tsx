import { useEffect } from 'react'
import { Navigate, Outlet } from 'react-router'
import { useIsAdmin } from '../stores/authStore'
import { useToast } from './toastContext'

/** Admin-only routes. A non-admin gets bounced to / with a warning toast. */
export function AdminRoute() {
  const isAdmin = useIsAdmin()
  const { showError } = useToast()

  useEffect(() => {
    if (!isAdmin) {
      showError('Bu sayfaya erişim yetkiniz yok.')
    }
  }, [isAdmin, showError])

  if (!isAdmin) {
    return <Navigate to="/" replace />
  }
  return <Outlet />
}
