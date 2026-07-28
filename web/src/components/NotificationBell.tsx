import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Dropdown, Spinner } from 'react-bootstrap'
import {
  getNotifications,
  markAllNotificationsRead,
  markNotificationRead,
} from '../api/notifications'
import type { NotificationDto } from '../types/api'
import { formatDateTime } from '../lib/format'

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
  const [open, setOpen] = useState(false)

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

  const unread = data?.unreadCount ?? 0
  const items = data?.items ?? []

  const openProduct = (n: NotificationDto) => {
    setOpen(false)
    if (n.readAt === null) readOne.mutate(n.id)
    navigate(`/urunler/${n.productId}`)
  }

  return (
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
        <div className="d-flex align-items-center justify-content-between px-3 py-2">
          <strong>Bildirimler</strong>
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
            <button
              key={n.id}
              type="button"
              onClick={() => openProduct(n)}
              className={`dropdown-item text-wrap border-bottom py-2 ${
                n.readAt === null ? 'fw-semibold' : 'text-muted'
              }`}
            >
              <div className="d-flex align-items-center gap-2 mb-1">
                <Badge bg={VARIANT[n.type]}>{LABEL[n.type]}</Badge>
                <span className="small text-muted">{formatDateTime(n.createdAt)}</span>
              </div>
              <div className="small">{describe(n)}</div>
            </button>
          ))
        )}
      </Dropdown.Menu>
    </Dropdown>
  )
}
