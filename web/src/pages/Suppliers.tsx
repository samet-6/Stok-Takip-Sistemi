import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Form, Modal, Spinner, Table } from 'react-bootstrap'
import {
  getSuppliers,
  createSupplier,
  updateSupplier,
  deleteSupplier,
} from '../api/suppliers'
import type { SupplierDto } from '../types/api'
import { useToast } from '../components/toastContext'
import { ConfirmModal } from '../components/ConfirmModal'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

const schema = z.object({
  name: z.string().min(1, 'Bu alan zorunludur').max(150, 'En fazla 150 karakter olabilir'),
  contactEmail: z
    .string()
    .min(1, 'Bu alan zorunludur')
    .email('Geçerli bir e-posta girin')
    .max(150, 'En fazla 150 karakter olabilir'),
  phone: z.string().max(20, 'En fazla 20 karakter olabilir'),
  address: z.string().max(300, 'En fazla 300 karakter olabilir'),
  isActive: z.boolean(),
})
type SupplierForm = z.infer<typeof schema>

export default function Suppliers() {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const listQuery = useQuery({ queryKey: ['suppliers'], queryFn: getSuppliers })

  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<SupplierDto | null>(null)
  const [deleting, setDeleting] = useState<SupplierDto | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<SupplierForm>({ resolver: zodResolver(schema) })

  const openCreate = () => {
    setEditing(null)
    reset({ name: '', contactEmail: '', phone: '', address: '', isActive: true })
    setShowForm(true)
  }
  const openEdit = (s: SupplierDto) => {
    setEditing(s)
    reset({
      name: s.name,
      contactEmail: s.contactEmail,
      phone: s.phone ?? '',
      address: s.address ?? '',
      isActive: s.isActive,
    })
    setShowForm(true)
  }

  const saveMutation = useMutation({
    mutationFn: (values: SupplierForm) => {
      const base = {
        name: values.name,
        contactEmail: values.contactEmail,
        phone: values.phone || null,
        address: values.address || null,
      }
      return editing
        ? updateSupplier(editing.id, { ...base, isActive: values.isActive })
        : createSupplier(base)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['suppliers'] })
      showSuccess(editing ? 'Tedarikçi güncellendi' : 'Tedarikçi eklendi')
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
                  <td>{s.name}</td>
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
                    <Button
                      size="sm"
                      variant="outline-danger"
                      onClick={() => setDeleting(s)}
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
            <Modal.Title>{editing ? 'Tedarikçi Düzenle' : 'Yeni Tedarikçi'}</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <Form.Group className="mb-3" controlId="supplier-name">
              <Form.Label>Ad</Form.Label>
              <Form.Control {...register('name')} isInvalid={!!errors.name} autoFocus />
              <Form.Control.Feedback type="invalid">
                {errors.name?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-3" controlId="supplier-email">
              <Form.Label>E-posta</Form.Label>
              <Form.Control
                type="email"
                {...register('contactEmail')}
                isInvalid={!!errors.contactEmail}
              />
              <Form.Control.Feedback type="invalid">
                {errors.contactEmail?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-3" controlId="supplier-phone">
              <Form.Label>Telefon</Form.Label>
              <Form.Control {...register('phone')} isInvalid={!!errors.phone} />
              <Form.Control.Feedback type="invalid">
                {errors.phone?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-3" controlId="supplier-address">
              <Form.Label>Adres</Form.Label>
              <Form.Control
                as="textarea"
                rows={2}
                {...register('address')}
                isInvalid={!!errors.address}
              />
              <Form.Control.Feedback type="invalid">
                {errors.address?.message}
              </Form.Control.Feedback>
            </Form.Group>
            {editing && (
              <Form.Check
                type="switch"
                id="supplier-isactive"
                label="Aktif"
                {...register('isActive')}
              />
            )}
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
