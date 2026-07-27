import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import {
  useQuery,
  useMutation,
  useQueryClient,
  keepPreviousData,
} from '@tanstack/react-query'
import { Alert, Button, Col, Form, Row, Spinner, Table } from 'react-bootstrap'
import { getProducts, getProductSummary, deleteProduct } from '../api/products'
import type { ProductQuery } from '../api/products'
import type { ProductListDto } from '../types/api'
import { getCategories } from '../api/categories'
import { useIsAdmin } from '../stores/authStore'
import { useToast } from '../components/toastContext'
import { ConfirmModal } from '../components/ConfirmModal'
import { PageHeader } from '../components/PageHeader'
import { StatusChip } from '../components/StatusChip'
import { StatTile } from '../components/StatTile'
import { formatCurrency } from '../lib/format'
import { Pager } from '../components/Pager'
import { canonicalParams } from '../lib/urlParams'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

const PAGE_SIZE = 10

// Canonical URL (single source of truth): fixed key order, and `page=1` (the default) is
// dropped so the address bar stays stable instead of churning param order / leaving `?page=1`.
const PARAM_ORDER = ['search', 'categoryId', 'lowStockOnly', 'includeInactive', 'page'] as const
const PARAM_DEFAULTS = { page: '1' }
const normalizeParams = (p: URLSearchParams) => canonicalParams(p, PARAM_ORDER, PARAM_DEFAULTS)

export default function Products() {
  const isAdmin = useIsAdmin()
  const [searchParams, setSearchParams] = useSearchParams()

  // --- Read list state from the URL (single source of truth) ---
  const search = searchParams.get('search') ?? ''
  const categoryId = searchParams.get('categoryId') ?? ''
  const lowStockOnly = searchParams.get('lowStockOnly') === 'true'
  const includeInactive = searchParams.get('includeInactive') === 'true'
  const page = Number(searchParams.get('page') ?? '1') || 1

  const query: ProductQuery = {
    search: search || undefined,
    categoryId: categoryId ? Number(categoryId) : undefined,
    lowStockOnly,
    includeInactive,
    page,
    pageSize: PAGE_SIZE,
  }

  // --- Debounced search box (local input → URL after 300ms) ---
  const [searchInput, setSearchInput] = useState(search)
  useEffect(() => {
    const t = setTimeout(() => {
      const current = searchParams.get('search') ?? ''
      if (searchInput === current) return
      const next = new URLSearchParams(searchParams)
      if (searchInput) next.set('search', searchInput)
      else next.delete('search')
      next.set('page', '1')
      setSearchParams(normalizeParams(next))
    }, 300)
    return () => clearTimeout(t)
  }, [searchInput, searchParams, setSearchParams])

  // Keep the box in sync when the URL's search changes from outside (e.g. the navbar
  // home link clears it) — otherwise the local input would survive and the debounce
  // effect above would re-push it, resurrecting a search the user meant to leave.
  useEffect(() => {
    setSearchInput(search)
  }, [search])

  // Changing any filter resets to page 1; paging keeps everything else.
  const setFilter = (mutate: (p: URLSearchParams) => void) => {
    const next = new URLSearchParams(searchParams)
    mutate(next)
    next.set('page', '1')
    setSearchParams(normalizeParams(next))
  }

  const goToPage = (n: number) => {
    const next = new URLSearchParams(searchParams)
    next.set('page', String(n))
    setSearchParams(normalizeParams(next))
  }

  const clearFilters = () => {
    setSearchInput('')
    setSearchParams(new URLSearchParams())
  }

  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: getCategories,
  })

  const productsQuery = useQuery({
    queryKey: ['products', query],
    queryFn: () => getProducts(query),
    placeholderData: keepPreviousData,
  })

  // Inventory summary tiles: whole-catalogue totals, independent of the filters below.
  // The counts and the total value are computed by the database, so they stay correct
  // however many products there are.
  const summaryQuery = useQuery({
    queryKey: ['products', 'summary', {}],
    queryFn: () => getProductSummary({}),
  })
  const summary = summaryQuery.data

  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()
  const [deleting, setDeleting] = useState<ProductListDto | null>(null)

  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteProduct(id),
    onSuccess: (outcome) => {
      qc.invalidateQueries({ queryKey: ['products'] })
      showSuccess(outcome === 'soft' ? 'Ürün pasife alındı' : 'Ürün silindi')
      setDeleting(null)
    },
    onError: (err) => {
      showError(problemMessage(parseProblemDetails(err)))
      setDeleting(null)
    },
  })

  const colSpan = isAdmin ? 8 : 7

  return (
    <>
      <PageHeader
        title="Ürünler"
        subtitle="Envanterdeki tüm ürünler, stok durumu ve tedarikçileri."
        action={
          isAdmin && (
            <Link to="/urunler/yeni" className="btn btn-primary">
              Yeni Ürün
            </Link>
          )
        }
      />

      {summary && (
        <div className="stat-tiles mb-4">
          <StatTile label="Toplam Ürün" value={summary.totalProducts} />
          <StatTile
            label="Düşük Stok"
            value={summary.lowStockCount}
            valueColor={summary.lowStockCount > 0 ? 'var(--warn)' : undefined}
          />
          <StatTile label="Pasif" value={summary.passiveCount} />
          <StatTile label="Stok Değeri" value={formatCurrency(summary.totalStockValue)} />
        </div>
      )}

      {/* Filter bar */}
      <Row className="g-2 mb-3 align-items-center">
        <Col xs={12} md={4}>
          <Form.Control
            type="search"
            placeholder="İsim veya SKU ara…"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </Col>
        <Col xs={12} md={3}>
          <Form.Select
            value={categoryId}
            onChange={(e) =>
              setFilter((p) => {
                if (e.target.value) p.set('categoryId', e.target.value)
                else p.delete('categoryId')
              })
            }
          >
            <option value="">Tüm kategoriler</option>
            {categoriesQuery.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Form.Select>
        </Col>
        <Col xs="auto">
          <Form.Check
            type="switch"
            id="lowStockOnly"
            label="Sadece düşük stok"
            checked={lowStockOnly}
            onChange={(e) =>
              setFilter((p) => {
                if (e.target.checked) p.set('lowStockOnly', 'true')
                else p.delete('lowStockOnly')
              })
            }
          />
        </Col>
        <Col xs="auto">
          <Form.Check
            type="switch"
            id="includeInactive"
            label="Pasifleri göster"
            checked={includeInactive}
            onChange={(e) =>
              setFilter((p) => {
                if (e.target.checked) p.set('includeInactive', 'true')
                else p.delete('includeInactive')
              })
            }
          />
        </Col>
      </Row>

      {productsQuery.isError ? (
        <Alert variant="danger">
          {problemMessage(parseProblemDetails(productsQuery.error))}
        </Alert>
      ) : productsQuery.isLoading ? (
        <div className="text-center py-5">
          <Spinner animation="border" role="status" />
        </div>
      ) : (
        <>
          <div className="table-card">
            <Table hover responsive className="align-middle">
              <thead>
                <tr>
                  <th>Ad</th>
                  <th>SKU</th>
                  <th>Kategori</th>
                  <th>Tedarikçi</th>
                  <th className="text-end">Fiyat</th>
                  <th className="text-end">Stok</th>
                  <th>Durum</th>
                  {isAdmin && <th className="text-end">İşlemler</th>}
                </tr>
              </thead>
              <tbody>
                {productsQuery.data!.items.length === 0 ? (
                  <tr>
                    <td colSpan={colSpan} className="text-center text-muted py-4">
                      Ürün bulunamadı.{' '}
                      <Button variant="link" className="p-0 align-baseline" onClick={clearFilters}>
                        Filtreleri temizle
                      </Button>
                    </td>
                  </tr>
                ) : (
                  productsQuery.data!.items.map((p) => {
                    const low = p.stockQuantity <= p.minStockLevel
                    return (
                      <tr key={p.id} className={p.isActive ? undefined : 'row-muted'}>
                        <td>
                          <Link to={`/urunler/${p.id}`}>{p.name}</Link>
                        </td>
                        <td className="text-muted">{p.sku}</td>
                        <td>{p.categoryName}</td>
                        <td>{p.supplierName}</td>
                        <td className="text-end">{formatCurrency(p.unitPrice)}</td>
                        <td className="text-end">
                          {low ? (
                            <StatusChip variant="warn">{p.stockQuantity} · Düşük</StatusChip>
                          ) : (
                            p.stockQuantity
                          )}
                        </td>
                        <td>
                          <StatusChip variant={p.isActive ? 'neutral' : 'crit'}>
                            {p.isActive ? 'Aktif' : 'Pasif'}
                          </StatusChip>
                        </td>
                        {isAdmin && (
                          <td className="text-end text-nowrap">
                            <Link
                              to={`/urunler/${p.id}/duzenle`}
                              className="btn btn-sm btn-outline-secondary me-2"
                            >
                              Düzenle
                            </Link>
                            <Button
                              size="sm"
                              variant="outline-danger"
                              onClick={() => setDeleting(p)}
                            >
                              Sil
                            </Button>
                          </td>
                        )}
                      </tr>
                    )
                  })
                )}
              </tbody>
            </Table>
          </div>

          <Pager
            page={page}
            totalPages={productsQuery.data!.totalPages}
            onChange={goToPage}
          />
        </>
      )}

      <ConfirmModal
        show={deleting !== null}
        title="Ürün Sil"
        body={
          <>
            <strong>{deleting?.name}</strong> ürününü silmek istediğinize emin misiniz?
            Hareketi olan ürün silinmez, pasife alınır.
          </>
        }
        confirming={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting.id)}
        onHide={() => setDeleting(null)}
      />
    </>
  )
}
