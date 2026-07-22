import { useState } from 'react'
import { Link } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Spinner, Table } from 'react-bootstrap'
import { getSuppliers, deleteSupplier } from '../api/suppliers'
import type { SupplierDto } from '../types/api'
import { useToast } from '../components/toastContext'
import { ConfirmModal } from '../components/ConfirmModal'
import { SupplierFormModal } from '../components/SupplierFormModal'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

export default function Suppliers() {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const listQuery = useQuery({ queryKey: ['suppliers'], queryFn: getSuppliers })

  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<SupplierDto | null>(null)
  const [deleting, setDeleting] = useState<SupplierDto | null>(null)

  const openCreate = () => {
    setEditing(null)
    setShowForm(true)
  }
  const openEdit = (s: SupplierDto) => {
    setEditing(s)
    setShowForm(true)
  }

  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteSupplier(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['suppliers'] })
      showSuccess('Tedarikçi silindi')
      setDeleting(null)
    },
    onError: (err) => {
      showError(problemMessage(parseProblemDetails(err)))
      setDeleting(null)
    },
  })

  return (
    <>
      <div className="d-flex justify-content-between align-items-center mb-4">
        <h2 className="mb-0">Tedarikçiler</h2>
        <Button variant="primary" onClick={openCreate}>
          Yeni Tedarikçi
        </Button>
      </div>

      {listQuery.isLoading ? (
        <div className="text-center py-5">
          <Spinner animation="border" />
        </div>
      ) : (
        <Table hover responsive className="align-middle">
          <thead>
            <tr>
              <th>Ad</th>
              <th>E-posta</th>
              <th>Telefon</th>
              <th className="text-end">Ürün sayısı</th>
              <th>Durum</th>
              <th className="text-end">İşlemler</th>
            </tr>
          </thead>
          <tbody>
            {listQuery.data!.length === 0 ? (
              <tr>
                <td colSpan={6} className="text-center text-muted py-4">
                  Tedarikçi yok.
                </td>
              </tr>
            ) : (
              listQuery.data!.map((s) => (
                <tr key={s.id} className={s.isActive ? undefined : 'table-secondary'}>
                  <td>
                    <Link to={`/tedarikciler/${s.id}`} className="text-decoration-none">
                      {s.name}
                    </Link>
                  </td>
                  <td className="text-muted">{s.contactEmail}</td>
                  <td className="text-muted">{s.phone}</td>
                  <td className="text-end">{s.productCount}</td>
                  <td>
                    <Badge bg={s.isActive ? 'success' : 'secondary'}>
                      {s.isActive ? 'Aktif' : 'Pasif'}
                    </Badge>
                  </td>
                  <td className="text-end text-nowrap">
                    <Button
                      size="sm"
                      variant="outline-secondary"
                      className="me-2"
                      onClick={() => openEdit(s)}
                    >
                      Düzenle
                    </Button>
                    <Button size="sm" variant="outline-danger" onClick={() => setDeleting(s)}>
                      Sil
                    </Button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </Table>
      )}

      <SupplierFormModal
        show={showForm}
        supplier={editing}
        onHide={() => setShowForm(false)}
      />

      <ConfirmModal
        show={deleting !== null}
        title="Tedarikçi Sil"
        body={
          <>
            <strong>{deleting?.name}</strong> tedarikçisini silmek istediğinize emin misiniz?
          </>
        }
        confirming={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting.id)}
        onHide={() => setDeleting(null)}
      />
    </>
  )
}
