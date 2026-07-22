import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Alert, Breadcrumb, Button, Card, Col, Row, Spinner } from 'react-bootstrap'
import { getSuppliers } from '../api/suppliers'
import { CatalogDetailView } from '../components/CatalogDetailView'
import { SupplierFormModal } from '../components/SupplierFormModal'
import { StatusChip } from '../components/StatusChip'

// Admin-only supplier detail. This page owns the entity identity (breadcrumb + contact card +
// edit modal); the tabbed products/movements body is the shared CatalogDetailView.
export default function TedarikciDetay() {
  const { id = '' } = useParams()
  const supplierId = Number(id)
  const [showEdit, setShowEdit] = useState(false)

  // Identity — reused from the cached list (admin arrives by clicking there).
  const suppliersQuery = useQuery({ queryKey: ['suppliers'], queryFn: getSuppliers })
  const supplier = suppliersQuery.data?.find((s) => s.id === supplierId)

  if (suppliersQuery.isLoading) {
    return (
      <div className="text-center py-5">
        <Spinner animation="border" />
      </div>
    )
  }

  if (!supplier) {
    return (
      <Alert variant="warning">
        Tedarikçi bulunamadı. <Link to="/tedarikciler">Tedarikçiler listesine dön</Link>.
      </Alert>
    )
  }

  return (
    <>
      <Breadcrumb>
        <Breadcrumb.Item linkAs={Link} linkProps={{ to: '/tedarikciler' }}>
          Tedarikçiler
        </Breadcrumb.Item>
        <Breadcrumb.Item active>{supplier.name}</Breadcrumb.Item>
      </Breadcrumb>

      <Card className="mb-4">
        <Card.Body>
          <div className="d-flex justify-content-between align-items-start mb-3">
            <div className="d-flex align-items-center gap-2">
              <h2 className="mb-0">{supplier.name}</h2>
              <StatusChip variant={supplier.isActive ? 'neutral' : 'crit'}>
                {supplier.isActive ? 'Aktif' : 'Pasif'}
              </StatusChip>
            </div>
            <Button variant="outline-secondary" size="sm" onClick={() => setShowEdit(true)}>
              Düzenle
            </Button>
          </div>
          <Row className="g-3">
            <Col xs={12} md={4}>
              <div className="text-muted small text-uppercase fw-bold">E-posta</div>
              <a href={`mailto:${supplier.contactEmail}`} className="text-decoration-none">
                {supplier.contactEmail}
              </a>
            </Col>
            <Col xs={6} md={4}>
              <div className="text-muted small text-uppercase fw-bold">Telefon</div>
              <div>{supplier.phone || '—'}</div>
            </Col>
            <Col xs={6} md={4}>
              <div className="text-muted small text-uppercase fw-bold">Adres</div>
              <div>{supplier.address || '—'}</div>
            </Col>
          </Row>
        </Card.Body>
      </Card>

      <CatalogDetailView scope={{ supplierId }} otherColumn="category" />

      <SupplierFormModal show={showEdit} supplier={supplier} onHide={() => setShowEdit(false)} />
    </>
  )
}
