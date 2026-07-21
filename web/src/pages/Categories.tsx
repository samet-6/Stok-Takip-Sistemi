import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button, Form, Modal, Spinner, Table } from 'react-bootstrap'
import {
  getCategories,
  createCategory,
  updateCategory,
  deleteCategory,
} from '../api/categories'
import type { CategoryDto } from '../types/api'
import { useToast } from '../components/toastContext'
import { ConfirmModal } from '../components/ConfirmModal'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

const schema = z.object({
  name: z.string().min(1, 'Bu alan zorunludur').max(100, 'En fazla 100 karakter olabilir'),
  description: z.string().max(500, 'En fazla 500 karakter olabilir'),
})
type CategoryForm = z.infer<typeof schema>

export default function Categories() {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const listQuery = useQuery({ queryKey: ['categories'], queryFn: getCategories })

  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<CategoryDto | null>(null)
  const [deleting, setDeleting] = useState<CategoryDto | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<CategoryForm>({ resolver: zodResolver(schema) })

  const openCreate = () => {
    setEditing(null)
    reset({ name: '', description: '' })
    setShowForm(true)
  }
  const openEdit = (c: CategoryDto) => {
    setEditing(c)
    reset({ name: c.name, description: c.description ?? '' })
    setShowForm(true)
  }

  const saveMutation = useMutation({
    mutationFn: (values: CategoryForm) => {
      const body = { name: values.name, description: values.description || null }
      return editing ? updateCategory(editing.id, body) : createCategory(body)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['categories'] })
      showSuccess(editing ? 'Kategori güncellendi' : 'Kategori eklendi')
      setShowForm(false)
    },
    onError: (err) => {
      const problem = parseProblemDetails(err)
      if (problem.status === 409) {
        setError('name', { type: 'server', message: problemMessage(problem) })
      } else {
        showError(problemMessage(problem))
      }
    },
  })

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
                  <td>{c.name}</td>
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
                    <Button
                      size="sm"
                      variant="outline-danger"
                      onClick={() => setDeleting(c)}
                    >
                      Sil
                    </Button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </Table>
      )}

      {/* Create / Edit modal */}
      <Modal show={showForm} onHide={() => setShowForm(false)} centered>
        <Form onSubmit={handleSubmit((v) => saveMutation.mutate(v))} noValidate>
          <Modal.Header closeButton>
            <Modal.Title>{editing ? 'Kategori Düzenle' : 'Yeni Kategori'}</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <Form.Group className="mb-3" controlId="category-name">
              <Form.Label>Ad</Form.Label>
              <Form.Control {...register('name')} isInvalid={!!errors.name} autoFocus />
              <Form.Control.Feedback type="invalid">
                {errors.name?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group controlId="category-description">
              <Form.Label>Açıklama</Form.Label>
              <Form.Control
                as="textarea"
                rows={3}
                {...register('description')}
                isInvalid={!!errors.description}
              />
              <Form.Control.Feedback type="invalid">
                {errors.description?.message}
              </Form.Control.Feedback>
            </Form.Group>
          </Modal.Body>
          <Modal.Footer>
            <Button variant="secondary" onClick={() => setShowForm(false)} disabled={saveMutation.isPending}>
              Vazgeç
            </Button>
            <Button type="submit" variant="primary" disabled={saveMutation.isPending}>
              {saveMutation.isPending ? (
                <>
                  <Spinner as="span" size="sm" animation="border" className="me-2" />
                  Kaydediliyor…
                </>
              ) : (
                'Kaydet'
              )}
            </Button>
          </Modal.Footer>
        </Form>
      </Modal>

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
