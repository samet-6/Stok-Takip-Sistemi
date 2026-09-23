import { Alert, Button } from 'react-bootstrap'

// The modal counterpart of ProductForm's conflict banner: same promise (what you typed stays),
// same explicit way out.
export function VersionConflictAlert({
  subject,
  onReload,
}: Readonly<{
  /** "kategori", "tedarikçi" — the row being edited. */
  subject: string
  onReload: () => void
}>) {
  return (
    <Alert variant="warning">
      <span>
        Bu {subject} siz düzenlerken başka bir yerden değiştirildi. Yazdıklarınız duruyor, ama bu
        hâliyle kaydedilemez. <strong>Güncel hâlini yükle</strong> son hâlini getirir ve
        yazdıklarınızın yerine geçer.
      </span>
      <div className="mt-2">
        <Button variant="outline-dark" size="sm" onClick={onReload}>
          Güncel hâlini yükle
        </Button>
      </div>
    </Alert>
  )
}
