import { useRef } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button, Form, Spinner } from 'react-bootstrap'
import { createStockMovement } from '../api/stockMovements'
import type { CreateStockMovementRequest } from '../types/api'
import { useToast } from '../components/toastContext'
import { PageHeader } from '../components/PageHeader'
import { ProductPicker } from '../components/ProductPicker'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'
import { newIdempotencyKey } from '../lib/idempotencyKey'

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

  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<MovementForm>({ resolver: zodResolver(schema), defaultValues: DEFAULTS })

  // One key per intended movement (D27). Kept across every failure — after a lost answer the
  // user cannot know whether the movement was saved, and re-sending the same key makes that
  // question harmless. Replaced only once the intent is settled: saved, or refused as a reused key.
  const idempotencyKey = useRef<string | null>(null)

  const mutation = useMutation({
    mutationFn: (v: MovementForm) => {
      const body: CreateStockMovementRequest = {
        productId: Number(v.productId),
        type: v.type,
        quantity: Number(v.quantity),
        note: v.note || null,
      }
      const key = (idempotencyKey.current ??= newIdempotencyKey())
      return createStockMovement(body, key)
    },
    onSuccess: (res, vars) => {
      idempotencyKey.current = null
      qc.invalidateQueries({ queryKey: ['products'] })
      qc.invalidateQueries({ queryKey: ['product', res.movement.productId] })
      showSuccess(`Hareket kaydedildi. Yeni stok: ${res.newStockQuantity}`)
      // Keep the product and direction selected — entering several movements for
      // the same product is the common case; only the per-entry fields are cleared.
      reset({ ...DEFAULTS, productId: vars.productId, type: vars.type })
    },
    onError: (err, vars) => {
      const problem = parseProblemDetails(err)

      if (problem.code === 'concurrency_conflict') {
        // Another request changed this product between our read and our write, so nothing
        // was saved. Refresh the product first — that updates the stock shown next to the
        // selection — then tell the user to check it and re-send.
        qc.invalidateQueries({ queryKey: ['products'] })
        qc.invalidateQueries({ queryKey: ['product', Number(vars.productId)] })
        showError(
          'Bu ürünün stoğu siz işlem yaparken değişti. Güncel stok yukarıda yenilendi — kontrol edip tekrar gönderin.',
        )
        return
      }

      if (problem.code === 'idempotency_key_reused') {
        // This key already holds a movement with other values — most likely the first send did
        // land before the form was changed. Sending again is a new intent, so it gets a new key.
        idempotencyKey.current = null
        showError(
          'Bu işlem daha önce farklı değerlerle kaydedilmiş. Çift kayıt olmaması için hareket listesini kontrol edin; gerekiyorsa tekrar gönderin.',
        )
        return
      }

      showError(problemMessage(problem))
    },
  })

  return (
    <div style={{ maxWidth: 640 }}>
      <PageHeader title="Stok Hareketi" subtitle="Ürün girişi veya çıkışı ekleyin." />

      <Form onSubmit={handleSubmit((v) => mutation.mutate(v))} noValidate>
        <Form.Group className="mb-3">
          <Form.Label>Ürün</Form.Label>
          <Controller
            name="productId"
            control={control}
            render={({ field }) => (
              <ProductPicker
                value={field.value ? Number(field.value) : null}
                onChange={(id) => field.onChange(id === null ? '' : String(id))}
                isInvalid={!!errors.productId}
              />
            )}
          />
          {errors.productId && (
            <div className="text-danger small mt-1">{errors.productId.message}</div>
          )}
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
    </div>
  )
}
