import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button, Form, Modal, Spinner } from 'react-bootstrap'
import { createSupplier, updateSupplier } from '../api/suppliers'
import type { SupplierDto } from '../types/api'
import { useToast } from './toastContext'
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

// Shared create/edit modal for suppliers, used by the Tedarikçiler list (create + edit)
// and the supplier detail page (edit). `supplier` null = create; otherwise edit that row.
export function SupplierFormModal({
  show,
  supplier,
  onHide,
}: {
  show: boolean
  supplier: SupplierDto | null
  onHide: () => void
}) {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<SupplierForm>({ resolver: zodResolver(schema) })

  // Re-seed the form each time the modal opens (for a given supplier, or for create).
  useEffect(() => {
    if (!show) return
    reset(
      supplier
        ? {
            name: supplier.name,
            contactEmail: supplier.contactEmail,
            phone: supplier.phone ?? '',
            address: supplier.address ?? '',
            isActive: supplier.isActive,
          }
        : { name: '', contactEmail: '', phone: '', address: '', isActive: true },
    )
  }, [show, supplier, reset])

  const saveMutation = useMutation({
    mutationFn: (values: SupplierForm) => {
      const base = {
        name: values.name,
        contactEmail: values.contactEmail,
        phone: values.phone || null,
        address: values.address || null,
      }
      return supplier
        ? updateSupplier(supplier.id, { ...base, isActive: values.isActive })
        : createSupplier(base)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['suppliers'] })
      showSuccess(supplier ? 'Tedarikçi güncellendi' : 'Tedarikçi eklendi')
      onHide()
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

  return (
    <Modal show={show} onHide={onHide} centered>
      <Form onSubmit={handleSubmit((v) => saveMutation.mutate(v))} noValidate>
        <Modal.Header closeButton>
          <Modal.Title>{supplier ? 'Tedarikçi Düzenle' : 'Yeni Tedarikçi'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <Form.Group className="mb-3" controlId="supplier-name">
            <Form.Label>Ad</Form.Label>
            <Form.Control {...register('name')} isInvalid={!!errors.name} autoFocus />
            <Form.Control.Feedback type="invalid">{errors.name?.message}</Form.Control.Feedback>
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
            <Form.Control.Feedback type="invalid">{errors.phone?.message}</Form.Control.Feedback>
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
          {supplier && (
            <Form.Check type="switch" id="supplier-isactive" label="Aktif" {...register('isActive')} />
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
