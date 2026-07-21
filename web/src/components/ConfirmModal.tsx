import type { ReactNode } from 'react'
import { Button, Modal, Spinner } from 'react-bootstrap'

interface ConfirmModalProps {
  show: boolean
  title: string
  body: ReactNode
  confirmLabel?: string
  confirmVariant?: string
  confirming?: boolean
  onConfirm: () => void
  onHide: () => void
}

/** Generic confirmation dialog (used for all destructive actions). */
export function ConfirmModal({
  show,
  title,
  body,
  confirmLabel = 'Sil',
  confirmVariant = 'danger',
  confirming = false,
  onConfirm,
  onHide,
}: ConfirmModalProps) {
  return (
    <Modal show={show} onHide={onHide} centered>
      <Modal.Header closeButton>
        <Modal.Title>{title}</Modal.Title>
      </Modal.Header>
      <Modal.Body>{body}</Modal.Body>
      <Modal.Footer>
        <Button variant="secondary" onClick={onHide} disabled={confirming}>
          Vazgeç
        </Button>
        <Button variant={confirmVariant} onClick={onConfirm} disabled={confirming}>
          {confirming ? (
            <>
              <Spinner as="span" size="sm" animation="border" className="me-2" />
              İşleniyor…
            </>
          ) : (
            confirmLabel
          )}
        </Button>
      </Modal.Footer>
    </Modal>
  )
}
