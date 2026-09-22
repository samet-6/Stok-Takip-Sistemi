import { useCallback, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Toast, ToastContainer } from 'react-bootstrap'
import { ToastContext, type ToastApi } from './toastContext'

type ToastVariant = 'success' | 'danger'

interface ToastItem {
  id: number
  message: string
  variant: ToastVariant
}

export function ToastProvider({ children }: Readonly<{ children: ReactNode }>) {
  const [toasts, setToasts] = useState<ToastItem[]>([])

  // A counter, not a timestamp: two toasts raised by the same action land in the same
  // millisecond, and ids that collide are React keys that collide.
  const nextId = useRef(0)

  const remove = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  const push = useCallback((message: string, variant: ToastVariant) => {
    nextId.current += 1
    const id = nextId.current
    setToasts((prev) => [...prev, { id, message, variant }])
  }, [])

  const showSuccess = useCallback((message: string) => push(message, 'success'), [push])
  const showError = useCallback((message: string) => push(message, 'danger'), [push])

  // Memoised because this provider wraps the whole app: a fresh object here re-renders every
  // component that calls useToast, on every render of anything above it.
  const api = useMemo<ToastApi>(() => ({ showSuccess, showError }), [showSuccess, showError])

  return (
    <ToastContext.Provider value={api}>
      {children}
      <ToastContainer position="top-end" className="p-3" style={{ zIndex: 1100 }}>
        {toasts.map((t) => (
          <Toast
            key={t.id}
            bg={t.variant}
            onClose={() => remove(t.id)}
            delay={4000}
            autohide
          >
            <Toast.Body className="text-white">{t.message}</Toast.Body>
          </Toast>
        ))}
      </ToastContainer>
    </ToastContext.Provider>
  )
}
