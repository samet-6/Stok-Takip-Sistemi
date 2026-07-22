import { useSearchParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Col, Form, Row, Spinner, Tab, Tabs } from 'react-bootstrap'
import { getProducts } from '../api/products'
import { getStockMovements } from '../api/stockMovements'
import { ProductMiniTable } from './ProductMiniTable'
import { MovementsTable } from './MovementsTable'
import { Pager } from './Pager'
import { formatCurrency, dayStartIso, dayEndIso } from '../lib/format'
import { canonicalParams } from '../lib/urlParams'
import type { MovementType } from '../types/api'

type TypeFilter = 'all' | MovementType

// Tab + filters live in the URL (single source of truth) → shareable + refresh-safe.
const PARAM_ORDER = ['tab', 'type', 'productId', 'from', 'to', 'page'] as const
const PARAM_DEFAULTS = { tab: 'urun', page: '1' }

// Shared tabbed body for the Tedarikçi/Kategori detail pages: summary tiles + Ürünler tab +
// Stok Hareketleri tab (filters + "Yapan"). Everything varies only by `scope` (which catalog
// dimension narrows products/movements) and `otherColumn` (the non-redundant product column).
// The entity identity card (supplier contact vs category description) stays in the page.
export function CatalogDetailView({
  scope,
  otherColumn,
}: {
  scope: { supplierId: number } | { categoryId: number }
  otherColumn: 'category' | 'supplier'
}) {
  const [searchParams, setSearchParams] = useSearchParams()

  const tab: 'urun' | 'har' = searchParams.get('tab') === 'har' ? 'har' : 'urun'
  const typeParam = searchParams.get('type')
  const typeFilter: TypeFilter = typeParam === 'In' || typeParam === 'Out' ? typeParam : 'all'
  const productFilter = searchParams.get('productId') ?? '' // '' = all products
  const fromDate = searchParams.get('from') ?? ''
  const toDate = searchParams.get('to') ?? ''
  const page = Number(searchParams.get('page') ?? '1') || 1

  const writeParams = (mutate: (p: URLSearchParams) => void, resetPage = true) => {
    const next = new URLSearchParams(searchParams)
    mutate(next)
    if (resetPage) next.set('page', '1')
    setSearchParams(canonicalParams(next, PARAM_ORDER, PARAM_DEFAULTS))
  }
  // Changing a filter resets to page 1; switching tab / paging keeps the rest.
  const setParam = (key: string, value: string) =>
    writeParams((p) => (value ? p.set(key, value) : p.delete(key)))
  const selectTab = (k: string) =>
    writeParams((p) => p.set('tab', k === 'har' ? 'har' : 'urun'), false)
  const goToPage = (n: number) => writeParams((p) => p.set('page', String(n)), false)
  const clearFilters = () =>
    setSearchParams(canonicalParams(new URLSearchParams({ tab: 'har' }), PARAM_ORDER, PARAM_DEFAULTS))

  // includeInactive: list ALL products of this supplier/category (passives get a badge), so
  // "Toplam Ürün" matches the productCount shown on the list page.
  const productsQuery = useQuery({
    queryKey: ['products', 'catalog-detail', scope],
    queryFn: () => getProducts({ ...scope, includeInactive: true, pageSize: 100 }),
  })
  const products = productsQuery.data?.items ?? []

  const movementsQuery = useQuery({
    queryKey: ['movements', 'catalog-detail', scope, typeFilter, productFilter, fromDate, toDate, page],
    queryFn: () =>
      getStockMovements({
        ...scope,
        type: typeFilter === 'all' ? undefined : typeFilter,
        productId: productFilter ? Number(productFilter) : undefined,
        from: fromDate ? dayStartIso(fromDate) : undefined,
        to: toDate ? dayEndIso(toDate) : undefined,
        page,
        pageSize: 10,
      }),
  })
  const movements = movementsQuery.data?.items ?? []

  // Düşük Stoklu + Stok Değeri over ACTIVE products only (archived ones aren't current
  // stock/value); Toplam Ürün counts all products (active + passive).
  const activeProducts = products.filter((p) => p.isActive)
  const totalValue = activeProducts.reduce((sum, p) => sum + p.unitPrice * p.stockQuantity, 0)
  const lowStockCount = activeProducts.filter((p) => p.stockQuantity <= p.minStockLevel).length

  return (
    <>
      {/* Summary tiles */}
      <Row className="g-3 mb-4">
        <Col xs={12} md={4}>
          <Card body>
            <div className="text-muted small text-uppercase fw-bold">Toplam Ürün</div>
            <div className="fs-4 fw-bold">{products.length}</div>
          </Card>
        </Col>
        <Col xs={6} md={4}>
          <Card body>
            <div className="text-muted small text-uppercase fw-bold">Düşük Stoklu</div>
            <div className="fs-4 fw-bold text-warning">{lowStockCount}</div>
          </Card>
        </Col>
        <Col xs={6} md={4}>
          <Card body>
            <div className="text-muted small text-uppercase fw-bold">Toplam Stok Değeri</div>
            <div className="fs-4 fw-bold">{formatCurrency(totalValue)}</div>
          </Card>
        </Col>
      </Row>

      <Tabs activeKey={tab} onSelect={(k) => selectTab(k ?? 'urun')} className="mb-3">
        <Tab eventKey="urun" title={`Ürünler (${products.length})`}>
          {productsQuery.isLoading ? (
            <div className="text-center py-5">
              <Spinner animation="border" />
            </div>
          ) : productsQuery.isError ? (
            <Alert variant="danger">Ürünler yüklenemedi.</Alert>
          ) : (
            <ProductMiniTable items={products} otherColumn={otherColumn} />
          )}
        </Tab>

        <Tab eventKey="har" title="Stok Hareketleri">
          {/* Filter bar */}
          <Row className="g-2 align-items-end mb-3">
            <Col xs={6} md="auto">
              <Form.Label className="small text-muted mb-1">Tip</Form.Label>
              <Form.Select
                value={typeFilter}
                onChange={(e) => setParam('type', e.target.value === 'all' ? '' : e.target.value)}
              >
                <option value="all">Tümü</option>
                <option value="In">Giriş</option>
                <option value="Out">Çıkış</option>
              </Form.Select>
            </Col>
            <Col xs={6} md="auto">
              <Form.Label className="small text-muted mb-1">Ürün</Form.Label>
              <Form.Select
                value={productFilter}
                onChange={(e) => setParam('productId', e.target.value)}
              >
                <option value="">Tüm ürünler</option>
                {products.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </Form.Select>
            </Col>
            <Col xs={6} md="auto">
              <Form.Label className="small text-muted mb-1">Başlangıç</Form.Label>
              <Form.Control
                type="date"
                value={fromDate}
                max={toDate || undefined}
                onChange={(e) => setParam('from', e.target.value)}
              />
            </Col>
            <Col xs={6} md="auto">
              <Form.Label className="small text-muted mb-1">Bitiş</Form.Label>
              <Form.Control
                type="date"
                value={toDate}
                min={fromDate || undefined}
                onChange={(e) => setParam('to', e.target.value)}
              />
            </Col>
            <Col xs="auto">
              <Button variant="outline-secondary" onClick={clearFilters}>
                Temizle
              </Button>
            </Col>
          </Row>

          {movementsQuery.isLoading ? (
            <div className="text-center py-5">
              <Spinner animation="border" />
            </div>
          ) : movementsQuery.isError ? (
            <Alert variant="danger">Hareketler yüklenemedi.</Alert>
          ) : movements.length === 0 ? (
            <Alert variant="secondary">Bu filtrelerle hareket bulunamadı.</Alert>
          ) : (
            <>
              <MovementsTable items={movements} showCreatedBy />
              <Pager
                page={page}
                totalPages={movementsQuery.data?.totalPages ?? 1}
                onChange={goToPage}
              />
            </>
          )}
        </Tab>
      </Tabs>
    </>
  )
}
