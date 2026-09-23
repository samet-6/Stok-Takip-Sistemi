import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button, Form, Modal, Spinner } from 'react-bootstrap'
import { createCategory, getCategories, updateCategory } from '../api/categories'
import type { CategoryDto } from '../types/api'
import { useToast } from './toastContext'
import { useVersionedEdit } from './useVersionedEdit'
import { VersionConflictAlert } from './VersionConflictAlert'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

const schema = z.object({
  name: z.string().min(1, 'Bu alan zorunludur').max(100, 'En fazla 100 karakter olabilir'),
  description: z.string().max(500, 'En fazla 500 karakter olabilir'),
  isActive: z.boolean(),
})
type CategoryForm = z.infer<typeof schema>

// Shared create/edit modal for categories, used by the Kategoriler list (create + edit) and
// the category detail page (edit). `category` null = create; otherwise edit that row.
export function CategoryFormModal({
  show,
  category,
  onHide,
}: Readonly<{
  show: boolean
  category: CategoryDto | null
  onHide: () => void
}>) {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<CategoryForm>({ resolver: zodResolver(schema) })

  const edit = useVersionedEdit({
    show,
    row: category,
    apply: (c) =>
      reset(
        c
          ? { name: c.name, description: c.description ?? '', isActive: c.isActive }
          : { name: '', description: '', isActive: true },
      ),
    reload: async (id) =>
      (await qc.fetchQuery({ queryKey: ['categories'], queryFn: getCategories, staleTime: 0 }))
        .find((c) => c.id === id),
    goneMessage: 'Bu kategori artık yok.',
  })

  const saveMutation = useMutation({
    mutationFn: (values: CategoryForm) => {
      // Categories are born active, like suppliers: the switch only exists while editing.
      const body = { name: values.name, description: values.description || null }
      return category
        ? updateCategory(category.id, {
            ...body,
            isActive: values.isActive,
            rowVersion: edit.rowVersion,
          })
        : createCategory(body)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['categories'] })
      showSuccess(category ? 'Kategori güncellendi' : 'Kategori eklendi')
      onHide()
    },
    onError: (err) => {
      const problem = parseProblemDetails(err)
      // Both are 409s; only the code tells a version clash from a taken name.
      if (problem.code === 'concurrency_conflict') {
        edit.markConflict()
      } else if (problem.status === 409) {
        setError('name', { type: 'server', message: problemMessage(problem) })
      } else {
        showError(problemMessage(problem))
      }
    },
  })

  return (
    <Modal show={show} onHide={onHide} centered>
      <Form onSubmit={handleSubmit((v) => saveMutation.mutate(v))} noValidate>
        <Modal.Header closeButton>
          <Modal.Title>{category ? 'Kategori Düzenle' : 'Yeni Kategori'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {edit.conflict && (
            <VersionConflictAlert subject="kategori" onReload={edit.reloadFromServer} />
          )}
          <Form.Group className="mb-3" controlId="category-name">
            <Form.Label>Ad</Form.Label>
            <Form.Control {...register('name')} isInvalid={!!errors.name} autoFocus />
            <Form.Control.Feedback type="invalid">{errors.name?.message}</Form.Control.Feedback>
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
          {category && (
            <Form.Check
              type="switch"
              id="category-isactive"
              label="Aktif"
              className="mt-3"
              {...register('isActive')}
            />
          )}
        </Modal.Body>
        <Modal.Footer>
          <Button variant="secondary" onClick={onHide} disabled={saveMutation.isPending}>
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
  )
}
