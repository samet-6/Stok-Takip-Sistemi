import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button, Form, Spinner } from 'react-bootstrap'
import { getProducts } from '../api/products'
import { createStockMovement } from '../api/stockMovements'
import type { CreateStockMovementRequest } from '../types/api'
import { useToast } from '../components/toastContext'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

const schema = z.object({
  productId: z.string().min(1, 'Ürün seçin'),
  type: z.enum(['In', 'Out']),
  quantity: z
    .string()
    .refine((v) => /^\d+$/.test(v) && Number(v) >= 1, 'Miktar en az 1 olmalıdır'),
  note: z.string().max(300, 'En fazla 300 karakter olabilir'),
})
type MovementForm = z.infer<typeof schema>

const DEFAULTS: MovementForm = { productId: '', type: 'In', quantity: '', note: '' }

export default function StockMovement() {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  // All products (passive included) — passive stock can still be drawn down.
  const productsQuery = useQuery({
    queryKey: ['products', 'all-for-movement'],
    queryFn: () => getProducts({ includeInactive: true, pageSize: 1000 }),
  })

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<MovementForm>({ resolver: zodResolver(schema), defaultValues: DEFAULTS })

  const mutation = useMutation({
    mutationFn: (v: MovementForm) => {
      const body: CreateStockMovementRequest = {
        productId: Number(v.productId),
        type: v.type,
        quantity: Number(v.quantity),
        note: v.note || null,
      }
      return createStockMovement(body)
    },
    onSuccess: (res) => {
      qc.invalidateQueries({ queryKey: ['products'] })
      qc.invalidateQueries({ queryKey: ['product', res.movement.productId] })
      showSuccess(`Hareket kaydedildi. Yeni stok: ${res.newStockQuantity}`)
      reset(DEFAULTS)
    },
    onError: (err) => {
      showError(problemMessage(parseProblemDetails(err)))
    },
  })

  return (
    <div style={{ maxWidth: 640 }}>
      <h2 className="mb-4">Stok Hareketi</h2>

      {productsQuery.isLoading ? (
        <div className="text-center py-5">
          <Spinner animation="border" />
        </div>
      ) : (
        <Form onSubmit={handleSubmit((v) => mutation.mutate(v))} noValidate>
          <Form.Group className="mb-3" controlId="movement-product">
            <Form.Label>Ürün</Form.Label>
            <Form.Select {...register('productId')} isInvalid={!!errors.productId}>
              <option value="">Seçiniz…</option>
              {productsQuery.data!.items.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name} ({p.sku}){p.isActive ? '' : ' (Pasif)'}
                </option>
              ))}
            </Form.Select>
            <Form.Control.Feedback type="invalid">
              {errors.productId?.message}
            </Form.Control.Feedback>
          </Form.Group>

          <Form.Group className="mb-3">
            <Form.Label className="d-block">Tip</Form.Label>
            <Form.Check
              inline
              type="radio"
              id="movement-type-in"
              label="Giriş"
              value="In"
              {...register('type')}
            />
            <Form.Check
              inline
              type="radio"
              id="movement-type-out"
              label="Çıkış"
              value="Out"
              {...register('type')}
            />
          </Form.Group>

          <Form.Group className="mb-3" controlId="movement-quantity">
            <Form.Label>Miktar</Form.Label>
            <Form.Control
              type="number"
              {...register('quantity')}
              isInvalid={!!errors.quantity}
            />
            <Form.Control.Feedback type="invalid">
              {errors.quantity?.message}
            </Form.Control.Feedback>
          </Form.Group>

          <Form.Group className="mb-4" controlId="movement-note">
            <Form.Label>Not (opsiyonel)</Form.Label>
            <Form.Control
              as="textarea"
              rows={2}
              {...register('note')}
              isInvalid={!!errors.note}
            />
            <Form.Control.Feedback type="invalid">
              {errors.note?.message}
            </Form.Control.Feedback>
          </Form.Group>

          <Button type="submit" variant="primary" disabled={mutation.isPending}>
            {mutation.isPending ? (
              <>
                <Spinner as="span" size="sm" animation="border" className="me-2" />
                Kaydediliyor…
              </>
            ) : (
              'Hareket Ekle'
            )}
          </Button>
        </Form>
      )}
    </div>
  )
}
