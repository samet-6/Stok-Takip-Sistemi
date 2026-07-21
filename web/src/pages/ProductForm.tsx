import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useNavigate, useParams } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Col, Form, Row, Spinner } from 'react-bootstrap'
import { getProduct, createProduct, updateProduct } from '../api/products'
import { getCategories } from '../api/categories'
import { getSuppliers } from '../api/suppliers'
import type { CreateProductRequest, UpdateProductRequest } from '../types/api'
import { useToast } from '../components/toastContext'
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

  // Populate the form from the loaded product (edit mode).
  useEffect(() => {
    if (productQuery.data) {
      const p = productQuery.data
      reset({
        name: p.name,
        sku: p.sku,
        description: p.description ?? '',
        categoryId: String(p.categoryId),
        supplierId: String(p.supplierId),
        unitPrice: String(p.unitPrice),
        minStockLevel: String(p.minStockLevel),
        initialStock: '',
        isActive: p.isActive,
      })
    }
  }, [productQuery.data, reset])

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
          rowVersion: productQuery.data!.rowVersion,
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
        // Stale rowVersion: pull the fresh record so a retry can succeed.
        productQuery.refetch()
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
      <h2 className="mb-4">{isEdit ? 'Ürün Düzenle' : 'Yeni Ürün'}</h2>

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
