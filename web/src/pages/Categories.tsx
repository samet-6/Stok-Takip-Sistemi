import { useState } from 'react'
import { Link } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button, Spinner, Table } from 'react-bootstrap'
import { getCategories, deleteCategory } from '../api/categories'
import type { CategoryDto } from '../types/api'
import { useToast } from '../components/toastContext'
import { ConfirmModal } from '../components/ConfirmModal'
import { CategoryFormModal } from '../components/CategoryFormModal'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

export default function Categories() {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const listQuery = useQuery({ queryKey: ['categories'], queryFn: getCategories })

  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<CategoryDto | null>(null)
  const [deleting, setDeleting] = useState<CategoryDto | null>(null)

  const openCreate = () => {
    setEditing(null)
    setShowForm(true)
  }
  const openEdit = (c: CategoryDto) => {
    setEditing(c)
    setShowForm(true)
  }

  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteCategory(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['categories'] })
      showSuccess('Kategori silindi')
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
        <h2 className="mb-0">Kategoriler</h2>
        <Button variant="primary" onClick={openCreate}>
          Yeni Kategori
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
              <th>Açıklama</th>
              <th className="text-end">Ürün sayısı</th>
              <th className="text-end">İşlemler</th>
            </tr>
          </thead>
          <tbody>
            {listQuery.data!.length === 0 ? (
              <tr>
                <td colSpan={4} className="text-center text-muted py-4">
                  Kategori yok.
                </td>
              </tr>
            ) : (
              listQuery.data!.map((c) => (
                <tr key={c.id}>
                  <td>
                    <Link to={`/kategoriler/${c.id}`} className="text-decoration-none">
                      {c.name}
                    </Link>
                  </td>
                  <td className="text-muted">{c.description}</td>
                  <td className="text-end">{c.productCount}</td>
                  <td className="text-end text-nowrap">
                    <Button
                      size="sm"
                      variant="outline-secondary"
                      className="me-2"
                      onClick={() => openEdit(c)}
                    >
                      Düzenle
                    </Button>
                    <Button size="sm" variant="outline-danger" onClick={() => setDeleting(c)}>
                      Sil
                    </Button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </Table>
      )}

      <CategoryFormModal
        show={showForm}
        category={editing}
        onHide={() => setShowForm(false)}
      />

      <ConfirmModal
        show={deleting !== null}
        title="Kategori Sil"
        body={
          <>
            <strong>{deleting?.name}</strong> kategorisini silmek istediğinize emin misiniz?
          </>
        }
        confirming={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting.id)}
        onHide={() => setDeleting(null)}
      />
    </>
  )
}
