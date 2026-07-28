import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useNavigate, useParams } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Col, Form, Row, Spinner } from 'react-bootstrap'
import { getProduct, createProduct, updateProduct } from '../api/products'
import { getCategories } from '../api/categories'
import { getSuppliers } from '../api/suppliers'
import type {
  CreateProductRequest,
  ProductDetailDto,
  UpdateProductRequest,
} from '../types/api'
import { useToast } from '../components/toastContext'
import { PageHeader } from '../components/PageHeader'
import { applyServerFieldErrors } from '../lib/formErrors'
import { parseProblemDetails, problemMessage, hasFieldErrors } from '../lib/problemDetails'

const isIntString = (v: string) => /^\d+$/.test(v)
const isNumberString = (v: string) => v !== '' && !Number.isNaN(Number(v))

const schema = z.object({
  name: z.string().min(1, 'Bu alan zorunludur').max(150, 'En fazla 150 karakter olabilir'),
  sku: z.string().min(1, 'Bu alan zorunludur').max(30, 'En fazla 30 karakter olabilir'),
  description: z.string().max(1000, 'En fazla 1000 karakter olabilir'),
  categoryId: z.string().min(1, 'Kategori seçin'),
  supplierId: z.string().min(1, 'Tedarikçi seçin'),
  unitPrice: z
    .string()
    .refine((v) => isNumberString(v) && Number(v) >= 0, 'Geçerli bir sayı girin'),
  minStockLevel: z.string().refine((v) => isIntString(v), 'Geçerli bir tam sayı girin'),
  initialStock: z
    .string()
    .refine((v) => v === '' || (isIntString(v) && Number(v) >= 1), 'En az 1 olmalıdır'),
  isActive: z.boolean(),
})
type ProductFormValues = z.infer<typeof schema>

const CREATE_DEFAULTS: ProductFormValues = {
  name: '',
  sku: '',
  description: '',
  categoryId: '',
  supplierId: '',
  unitPrice: '',
  minStockLevel: '',
  initialStock: '',
  isActive: true,
}

const FORM_FIELDS = [
  'name',
  'sku',
  'description',
  'categoryId',
  'supplierId',
  'unitPrice',
  'minStockLevel',
  'initialStock',
] as const

/**
 * The single definition of "what this form holds". Used both to populate the form and to
 * compare an incoming record against the one being edited, so the two can never drift:
 * a field added to the form joins the comparison in the same edit.
 */
function toFormValues(p: ProductDetailDto): ProductFormValues {
  return {
    name: p.name,
    sku: p.sku,
    description: p.description ?? '',
    categoryId: String(p.categoryId),
    supplierId: String(p.supplierId),
    unitPrice: String(p.unitPrice),
    minStockLevel: String(p.minStockLevel),
    initialStock: '',
    isActive: p.isActive,
  }
}

/** Every value is a string or a boolean, so a shallow comparison is the whole story. */
function sameFormValues(a: ProductFormValues, b: ProductFormValues): boolean {
  return (Object.keys(a) as (keyof ProductFormValues)[]).every((k) => a[k] === b[k])
}

/**
 * What the form was populated from, and the row version this edit is based on. Carries the
 * product id too: the route can swap :id without remounting, and a baseline belonging to the
 * previous product would send its rowVersion to the new one.
 */
type Baseline = { productId: number; values: ProductFormValues; rowVersion: number }

export default function ProductForm() {
  const { id } = useParams()
  const isEdit = id !== undefined
  const productId = id ? Number(id) : undefined
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: getCategories })
  const suppliersQuery = useQuery({ queryKey: ['suppliers'], queryFn: getSuppliers })
  const productQuery = useQuery({
    queryKey: ['product', productId],
    queryFn: () => getProduct(productId!),
    enabled: isEdit,
  })

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<ProductFormValues>({
    resolver: zodResolver(schema),
    defaultValues: CREATE_DEFAULTS,
  })

  const [baseline, setBaseline] = useState<Baseline | null>(null)
  const [conflict, setConflict] = useState(false)

  // Populate once, then never again on the user. The product query is refetched by realtime
  // signals and on window focus, so a reset() on every data arrival would wipe whatever is
  // half-typed — which is exactly what it used to do.
  useEffect(() => {
    const p = productQuery.data
    if (!p) return

    const incoming = toFormValues(p)

    if (baseline === null || baseline.productId !== p.id) {
      reset(incoming)
      setBaseline({ productId: p.id, values: incoming, rowVersion: p.rowVersion })
      setConflict(false)
      return
    }

    if (p.rowVersion === baseline.rowVersion) return

    // The row moved, but none of the fields this form writes did — so the only thing that
    // changed is stock, i.e. somebody entered a movement. Disjoint write sets: there is no
    // conflict to report, and saving must not be rejected for one. Adopt the fresh version
    // silently. (rowVersion is the whole row's xmin, which is why this case exists at all.)
    if (sameFormValues(incoming, baseline.values)) {
      setBaseline({ ...baseline, rowVersion: p.rowVersion })
      return
    }

    // A field the form writes changed underneath the user: a real conflict. Warn and stop
    // there — the typed values stay, and the decision is theirs.
    setConflict(true)
  }, [productQuery.data, baseline, reset])

  // Explicit, never automatic: takes the record the background refetch already put in the
  // cache (so no network round trip) and overwrites the form with it.
  const refreshFromServer = () => {
    const p = productQuery.data
    if (!p) return
    const incoming = toFormValues(p)
    reset(incoming)
    setBaseline({ productId: p.id, values: incoming, rowVersion: p.rowVersion })
    setConflict(false)
  }

  const saveMutation = useMutation({
    mutationFn: (v: ProductFormValues) => {
      if (isEdit) {
        const body: UpdateProductRequest = {
          name: v.name,
          sku: v.sku,
          description: v.description || null,
          categoryId: Number(v.categoryId),
          supplierId: Number(v.supplierId),
          unitPrice: Number(v.unitPrice),
          minStockLevel: Number(v.minStockLevel),
          isActive: v.isActive,
          // The version the form was populated from — never the freshest one. Sending
          // whatever the last background refetch happened to bring back would overwrite a
          // change the user never saw, silently defeating the server's 409.
          rowVersion: baseline!.rowVersion,
        }
        return updateProduct(productId!, body)
      }
      const body: CreateProductRequest = {
        name: v.name,
        sku: v.sku,
        description: v.description || null,
        categoryId: Number(v.categoryId),
        supplierId: Number(v.supplierId),
        unitPrice: Number(v.unitPrice),
        minStockLevel: Number(v.minStockLevel),
        initialStock: v.initialStock === '' ? undefined : Number(v.initialStock),
      }
      return createProduct(body)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['products'] })
      if (isEdit) qc.invalidateQueries({ queryKey: ['product', productId] })
      showSuccess(isEdit ? 'Ürün güncellendi' : 'Ürün eklendi')
      navigate('/')
    },
    onError: (err) => {
      const problem = parseProblemDetails(err)
      if (problem.status === 400 && hasFieldErrors(problem)) {
        applyServerFieldErrors(problem, setError, FORM_FIELDS)
        return
      }
      if (problem.code === 'concurrency_conflict') {
        // Stale rowVersion: pull the fresh record so a retry can succeed. The refetch also
        // raises the warning banner, which is where the recovery action lives.
        productQuery.refetch()
        // Screen-specific wording on purpose: the server only reports *what* happened, and the
        // same 409 means a different next step on the stock-movement screen. Pointing at the
        // banner beats the generic advice, which would send the user to a page reload.
        showError(
          'Bu ürün siz düzenlerken değiştirildi. Yukarıdaki "Formu yenile" ile güncel değerleri alabilirsiniz.',
        )
        return
      }
      showError(problemMessage(problem))
    },
  })

  const loading =
    categoriesQuery.isLoading ||
    suppliersQuery.isLoading ||
    (isEdit && productQuery.isLoading)

  if (loading) {
    return (
      <div className="text-center py-5">
        <Spinner animation="border" />
      </div>
    )
  }

  if (isEdit && productQuery.isError) {
    return <Alert variant="danger">Ürün yüklenemedi.</Alert>
  }

  const currentSupplierId = productQuery.data?.supplierId

  return (
    <div style={{ maxWidth: 640 }}>
      <PageHeader title={isEdit ? 'Ürün Düzenle' : 'Yeni Ürün'} />

      {conflict && (
        <Alert variant="warning">
          <div className="d-flex align-items-center justify-content-between gap-3 flex-wrap">
            <span>
              Bu ürün siz düzenlerken başka bir yerden değiştirildi. Yazdıklarınız duruyor,
              ama bu hâliyle kaydedilemez. <strong>Formu yenile</strong> güncel değerleri
              getirir ve yazdıklarınızın yerine geçer.
            </span>
            <Button variant="outline-dark" size="sm" onClick={refreshFromServer}>
              Formu yenile
            </Button>
          </div>
        </Alert>
      )}

      <Form onSubmit={handleSubmit((v) => saveMutation.mutate(v))} noValidate>
        <Form.Group className="mb-3" controlId="product-name">
          <Form.Label>Ad</Form.Label>
          <Form.Control {...register('name')} isInvalid={!!errors.name} autoFocus />
          <Form.Control.Feedback type="invalid">{errors.name?.message}</Form.Control.Feedback>
        </Form.Group>

        <Form.Group className="mb-3" controlId="product-sku">
          <Form.Label>SKU</Form.Label>
          <Form.Control {...register('sku')} isInvalid={!!errors.sku} />
          <Form.Control.Feedback type="invalid">{errors.sku?.message}</Form.Control.Feedback>
        </Form.Group>

        <Form.Group className="mb-3" controlId="product-description">
          <Form.Label>Açıklama</Form.Label>
          <Form.Control
            as="textarea"
            rows={2}
            {...register('description')}
            isInvalid={!!errors.description}
          />
          <Form.Control.Feedback type="invalid">
            {errors.description?.message}
          </Form.Control.Feedback>
        </Form.Group>

        <Row>
          <Col md={6}>
            <Form.Group className="mb-3" controlId="product-category">
              <Form.Label>Kategori</Form.Label>
              <Form.Select {...register('categoryId')} isInvalid={!!errors.categoryId}>
                <option value="">Seçiniz…</option>
                {categoriesQuery.data!.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Form.Select>
              <Form.Control.Feedback type="invalid">
                {errors.categoryId?.message}
              </Form.Control.Feedback>
            </Form.Group>
          </Col>
          <Col md={6}>
            <Form.Group className="mb-3" controlId="product-supplier">
              <Form.Label>Tedarikçi</Form.Label>
              <Form.Select {...register('supplierId')} isInvalid={!!errors.supplierId}>
                <option value="">Seçiniz…</option>
                {suppliersQuery.data!.map((s) => {
                  // Passive suppliers are disabled — except the product's current one in
                  // edit mode (a product whose supplier went passive stays editable).
                  const isCurrent = s.id === currentSupplierId
                  const disabled = !s.isActive && !isCurrent
                  return (
                    <option key={s.id} value={s.id} disabled={disabled}>
                      {s.name}
                      {!s.isActive ? ' (Pasif)' : ''}
                    </option>
                  )
                })}
              </Form.Select>
              <Form.Control.Feedback type="invalid">
                {errors.supplierId?.message}
              </Form.Control.Feedback>
            </Form.Group>
          </Col>
        </Row>

        <Row>
          <Col md={6}>
            <Form.Group className="mb-3" controlId="product-price">
              <Form.Label>Birim Fiyat (₺)</Form.Label>
              <Form.Control
                type="number"
                step="0.01"
                {...register('unitPrice')}
                isInvalid={!!errors.unitPrice}
              />
              <Form.Control.Feedback type="invalid">
                {errors.unitPrice?.message}
              </Form.Control.Feedback>
            </Form.Group>
          </Col>
          <Col md={6}>
            <Form.Group className="mb-3" controlId="product-minstock">
              <Form.Label>Minimum Stok</Form.Label>
              <Form.Control
                type="number"
                {...register('minStockLevel')}
                isInvalid={!!errors.minStockLevel}
              />
              <Form.Control.Feedback type="invalid">
                {errors.minStockLevel?.message}
              </Form.Control.Feedback>
            </Form.Group>
          </Col>
        </Row>

        {isEdit ? (
          <>
            <Alert variant="light" className="border">
              Stok: <strong>{productQuery.data!.stockQuantity}</strong> — hareketlerle yönetilir.
            </Alert>
            <Form.Check
              type="switch"
              id="product-isactive"
              label="Aktif"
              className="mb-3"
              {...register('isActive')}
            />
          </>
        ) : (
          <Form.Group className="mb-3" controlId="product-initialstock">
            <Form.Label>Başlangıç Stoğu (opsiyonel)</Form.Label>
            <Form.Control
              type="number"
              placeholder="Boş bırakılabilir"
              {...register('initialStock')}
              isInvalid={!!errors.initialStock}
            />
            <Form.Control.Feedback type="invalid">
              {errors.initialStock?.message}
            </Form.Control.Feedback>
          </Form.Group>
        )}

        <div className="d-flex gap-2 mt-4">
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
          <Button variant="outline-secondary" onClick={() => navigate('/')}>
            Vazgeç
          </Button>
        </div>
      </Form>
    </div>
  )
}
