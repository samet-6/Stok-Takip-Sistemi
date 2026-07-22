import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Col, Row, Spinner, Table } from 'react-bootstrap'
import { getProduct } from '../api/products'
import { useIsAdmin } from '../stores/authStore'
import { PageHeader } from '../components/PageHeader'
import { StatusChip } from '../components/StatusChip'
import { formatCurrency, formatDateTime } from '../lib/format'

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Col md={6} className="mb-3">
      <div className="text-muted small">{label}</div>
      <div>{children}</div>
    </Col>
  )
}

export default function ProductDetail() {
  const { id } = useParams()
  const productId = Number(id)
  const isAdmin = useIsAdmin()

  const productQuery = useQuery({
    queryKey: ['product', productId],
    queryFn: () => getProduct(productId),
  })

  if (productQuery.isLoading) {
    return (
      <div className="text-center py-5">
        <Spinner animation="border" />
      </div>
    )
  }

  if (productQuery.isError || !productQuery.data) {
    return <Alert variant="danger">Ürün yüklenemedi.</Alert>
  }

  const p = productQuery.data
  const low = p.stockQuantity <= p.minStockLevel

  return (
    <>
      <PageHeader
        title={p.name}
        action={
          isAdmin && (
            <Link to={`/urunler/${p.id}/duzenle`} className="btn btn-outline-secondary">
              Düzenle
            </Link>
          )
        }
      />

      <Card className="mb-4">
        <Card.Body>
          <Row>
            <Field label="SKU">{p.sku}</Field>
            <Field label="Durum">
              <StatusChip variant={p.isActive ? 'neutral' : 'crit'}>
                {p.isActive ? 'Aktif' : 'Pasif'}
              </StatusChip>
            </Field>
            <Field label="Kategori">{p.categoryName}</Field>
            <Field label="Tedarikçi">{p.supplierName}</Field>
            <Field label="Fiyat">{formatCurrency(p.unitPrice)}</Field>
            <Field label="Stok">
              <span className="tnum">{p.stockQuantity}</span>{' '}
              {low && <StatusChip variant="warn">Düşük</StatusChip>}
              <span className="text-muted small ms-2">(min {p.minStockLevel})</span>
            </Field>
            {p.description && <Field label="Açıklama">{p.description}</Field>}
            <Field label="Oluşturulma">{formatDateTime(p.createdAt)}</Field>
            <Field label="Güncellenme">{formatDateTime(p.updatedAt)}</Field>
          </Row>
        </Card.Body>
      </Card>

      <h3 className="h5 mb-3">Son Hareketler</h3>
      {p.recentMovements.length === 0 ? (
        <Alert variant="secondary">Henüz hareket yok.</Alert>
      ) : (
        <div className="table-card">
          <Table hover responsive className="align-middle">
            <thead>
              <tr>
                <th>Tip</th>
                <th className="text-end">Miktar</th>
                <th>Not</th>
                <th>Tarih</th>
                <th>Ekleyen</th>
              </tr>
            </thead>
            <tbody>
              {p.recentMovements.map((m) => (
                <tr key={m.id}>
                  <td>
                    <StatusChip variant={m.type === 'In' ? 'ok' : 'crit'}>
                      {m.type === 'In' ? 'Giriş' : 'Çıkış'}
                    </StatusChip>
                  </td>
                  <td className="text-end">{m.quantity}</td>
                  <td className="text-muted">{m.note}</td>
                  <td>{formatDateTime(m.createdAt)}</td>
                  <td>{m.createdByFullName}</td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      )}
    </>
  )
}
