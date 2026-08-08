import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Dropdown, Spinner } from 'react-bootstrap'
import {
  deleteNotification,
  deleteReadNotifications,
  getNotifications,
  markAllNotificationsRead,
  markNotificationRead,
} from '../api/notifications'
import type { NotificationDto } from '../types/api'
import { formatDateTime } from '../lib/format'
import { ConfirmModal } from './ConfirmModal'
import { useToast } from './toastContext'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

// One page is the whole panel: the bell is a "what needs attention now" surface, not an archive.
const PAGE_SIZE = 10

function describe(n: NotificationDto): string {
  switch (n.type) {
    case 'OutOfStock':
      return `${n.productName} tükendi (stok 0).`
    case 'LowStock':
      return `${n.productName} minimum stok seviyesinin altına indi (kalan ${n.quantity}).`
    case 'RejectedOutMovement':
      return `${n.createdByFullName} ${n.productName} için ${n.requestedQuantity} çıkış denedi, stok ${n.quantity}.`
  }
}

// Colour follows urgency, not variety: red only where stock is actually gone.
const VARIANT: Record<NotificationDto['type'], string> = {
  OutOfStock: 'danger',
  LowStock: 'warning',
  RejectedOutMovement: 'secondary',
}

const LABEL: Record<NotificationDto['type'], string> = {
  OutOfStock: 'Tükendi',
  LowStock: 'Düşük stok',
  RejectedOutMovement: 'Reddedilen çıkış',
}

export function NotificationBell() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const [open, setOpen] = useState(false)
  const [confirmingClear, setConfirmingClear] = useState(false)

  // Always mounted so the badge stays live: the realtime signal invalidates this key, and an
  // unmounted query would not refetch — the bell would only update when opened.
  const { data, isLoading } = useQuery({
    queryKey: ['notifications', { page: 1, pageSize: PAGE_SIZE }],
    queryFn: () => getNotifications({ page: 1, pageSize: PAGE_SIZE }),
  })

  const readOne = useMutation({
    mutationFn: markNotificationRead,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notifications'] }),
  })
  const readAll = useMutation({
    mutationFn: markAllNotificationsRead,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notifications'] }),
  })

  const removeOne = useMutation({
    mutationFn: deleteNotification,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notifications'] }),
    onError: (err) => showError(problemMessage(parseProblemDetails(err))),
  })
  const removeRead = useMutation({
    mutationFn: deleteReadNotifications,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['notifications'] })
      showSuccess('Okunmuş bildirimler silindi')
      setConfirmingClear(false)
    },
    onError: (err) => {
      showError(problemMessage(parseProblemDetails(err)))
      setConfirmingClear(false)
    },
  })

  const unread = data?.unreadCount ?? 0
  const items = data?.items ?? []

  // Both counters describe the whole table, not this page, so the button appears whenever there
  // is anything to clear — even when the read rows sit past the ten shown here.
  const readCount = (data?.totalCount ?? 0) - unread

  const openProduct = (n: NotificationDto) => {
    setOpen(false)
    if (n.readAt === null) readOne.mutate(n.id)
    navigate(`/urunler/${n.productId}`)
  }

  return (
    <>
      <Dropdown align="end" show={open} onToggle={setOpen} className="me-3">
        <Dropdown.Toggle
          as={Button}
          variant="outline-light"
          size="sm"
          className="position-relative"
          aria-label={`Bildirimler${unread > 0 ? `, ${unread} okunmamış` : ''}`}
        >
          🔔
          {unread > 0 && (
            <Badge bg="danger" pill className="position-absolute top-0 start-100 translate-middle">
              {unread > 99 ? '99+' : unread}
            </Badge>
          )}
        </Dropdown.Toggle>

        <Dropdown.Menu style={{ width: 380, maxHeight: 460, overflowY: 'auto' }}>
          <div className="d-flex align-items-center justify-content-between gap-2 px-3 py-2">
            <strong>Bildirimler</strong>
            <div className="d-flex gap-3">
              {unread > 0 && (
                <Button
                  variant="link"
                  size="sm"
                  className="p-0"
                  disabled={readAll.isPending}
                  onClick={() => readAll.mutate()}
                >
                  Tümünü okundu işaretle
                </Button>
              )}
              {readCount > 0 && (
                <Button
                  variant="link"
                  size="sm"
                  className="p-0 link-danger"
                  disabled={removeRead.isPending}
                  onClick={() => setConfirmingClear(true)}
                >
                  Okunmuşları sil
                </Button>
              )}
            </div>
          </div>
          <Dropdown.Divider className="my-0" />

          {isLoading ? (
            <div className="text-center py-4">
              <Spinner animation="border" size="sm" />
            </div>
          ) : items.length === 0 ? (
            <div className="text-muted small px-3 py-4 text-center">Bildirim yok.</div>
          ) : (
            items.map((n) => (
              // The row is no longer a single button: a delete control nested inside one would be
              // invalid HTML. The clickable area and the × are siblings instead, which leaves the
              // look unchanged and keeps both reachable by keyboard.
              <div key={n.id} className="d-flex align-items-start border-bottom">
                <button
                  type="button"
                  onClick={() => openProduct(n)}
                  className={`dropdown-item text-wrap flex-grow-1 py-2 ${
                    n.readAt === null ? 'fw-semibold' : 'text-muted'
                  }`}
                >
                  <div className="d-flex align-items-center gap-2 mb-1">
                    <Badge bg={VARIANT[n.type]}>{LABEL[n.type]}</Badge>
                    <span className="small text-muted">{formatDateTime(n.createdAt)}</span>
                  </div>
                  <div className="small">{describe(n)}</div>
                </button>
                <Button
                  variant="link"
                  size="sm"
                  className="text-secondary px-2 py-2"
                  aria-label="Bildirimi sil"
                  title="Bildirimi sil"
                  // Deleting a row that is already gone answers 404, so the button closes itself
                  // while the request is in flight rather than relying on a forgiving server.
                  disabled={removeOne.isPending}
                  onClick={() => removeOne.mutate(n.id)}
                >
                  ×
                </Button>
              </div>
            ))
          )}
        </Dropdown.Menu>
      </Dropdown>

      <ConfirmModal
        show={confirmingClear}
        title="Okunmuş Bildirimleri Sil"
        body={
          <>
            Okunmuş <strong>{readCount}</strong> bildirim kalıcı olarak silinecek. Okunmamış
            bildirimler kalır.
          </>
        }
        confirming={removeRead.isPending}
        onConfirm={() => removeRead.mutate()}
        onHide={() => setConfirmingClear(false)}
      />
    </>
  )
}
