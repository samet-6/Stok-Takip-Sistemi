import { useState } from 'react'
import { Link } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button, Spinner, Table } from 'react-bootstrap'
import { getSuppliers, deleteSupplier } from '../api/suppliers'
import type { SupplierDto } from '../types/api'
import { useToast } from '../components/toastContext'
import { ConfirmModal } from '../components/ConfirmModal'
import { SupplierFormModal } from '../components/SupplierFormModal'
import { PageHeader } from '../components/PageHeader'
import { StatusChip } from '../components/StatusChip'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'
import { useIsAdmin } from '../stores/authStore'

export default function Suppliers() {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()
  const isAdmin = useIsAdmin()

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
      <PageHeader
        title="Tedarikçiler"
        subtitle="Ürün tedarik eden firmalar ve iletişim bilgileri."
        action={
          isAdmin ? (
            <Button variant="primary" onClick={openCreate}>
              Yeni Tedarikçi
            </Button>
          ) : undefined
        }
      />

      {listQuery.isLoading ? (
        <div className="text-center py-5">
          <Spinner animation="border" />
        </div>
      ) : (
        <div className="table-card">
          <Table hover responsive className="align-middle">
            <thead>
              <tr>
                <th>Ad</th>
                {isAdmin && <th>E-posta</th>}
                {isAdmin && <th>Telefon</th>}
                <th className="text-end">Ürün sayısı</th>
                <th>Durum</th>
                {isAdmin && <th className="text-end">İşlemler</th>}
              </tr>
            </thead>
            <tbody>
              {listQuery.data!.length === 0 ? (
                <tr>
                  <td colSpan={isAdmin ? 6 : 3} className="text-center text-muted py-4">
                    Tedarikçi yok.
                  </td>
                </tr>
              ) : (
                listQuery.data!.map((s) => (
                  <tr key={s.id} className={s.isActive ? undefined : 'row-muted'}>
                    <td>
                      <Link to={`/tedarikciler/${s.id}`} className="text-decoration-none">
                        {s.name}
                      </Link>
                    </td>
                    {isAdmin && <td className="text-muted">{s.contactEmail}</td>}
                    {isAdmin && <td className="text-muted">{s.phone}</td>}
                    <td className="text-end">{s.productCount}</td>
                    <td>
                      <StatusChip variant={s.isActive ? 'neutral' : 'crit'}>
                        {s.isActive ? 'Aktif' : 'Pasif'}
                      </StatusChip>
                    </td>
                    {isAdmin && (
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
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </Table>
        </div>
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
