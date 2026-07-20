import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import {
  Alert,
  Badge,
  Button,
  Col,
  Form,
  Pagination,
  Row,
  Spinner,
  Table,
} from 'react-bootstrap'
import { getProducts } from '../api/products'
import type { ProductQuery } from '../api/products'
import { getCategories } from '../api/categories'
import { useIsAdmin } from '../stores/authStore'
import { formatCurrency } from '../lib/format'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

const PAGE_SIZE = 10

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
      setSearchParams(next)
    }, 300)
    return () => clearTimeout(t)
  }, [searchInput, searchParams, setSearchParams])

  // Changing any filter resets to page 1; paging keeps everything else.
  const setFilter = (mutate: (p: URLSearchParams) => void) => {
    const next = new URLSearchParams(searchParams)
    mutate(next)
    next.set('page', '1')
    setSearchParams(next)
  }

  const goToPage = (n: number) => {
    const next = new URLSearchParams(searchParams)
    next.set('page', String(n))
    setSearchParams(next)
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

  const colSpan = isAdmin ? 8 : 7

  return (
    <>
      <div className="d-flex justify-content-between align-items-center mb-4">
        <h2 className="mb-0">Ürünler</h2>
        {isAdmin && (
          <Link to="/urunler/yeni" className="btn btn-primary">
            Yeni Ürün
          </Link>
        )}
      </div>

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
          <Table hover responsive className="align-middle">
            <thead>
              <tr>
                <th>Ad</th>
                <th>SKU</th>
                <th>Kategori</th>
                <th>Tedarikçi</th>
                <th className="text-end">Fiyat</th>
                <th>Stok</th>
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
                    <tr key={p.id} className={p.isActive ? undefined : 'table-secondary'}>
                      <td>
                        <Link to={`/urunler/${p.id}`}>{p.name}</Link>
                      </td>
                      <td>{p.sku}</td>
                      <td>{p.categoryName}</td>
                      <td>{p.supplierName}</td>
                      <td className="text-end">{formatCurrency(p.unitPrice)}</td>
                      <td>
                        {p.stockQuantity}{' '}
                        <Badge bg={low ? 'danger' : 'success'}>
                          {low ? 'Düşük' : 'Yeterli'}
                        </Badge>
                      </td>
                      <td>
                        <Badge bg={p.isActive ? 'success' : 'secondary'}>
                          {p.isActive ? 'Aktif' : 'Pasif'}
                        </Badge>
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
                            disabled
                            title="F4c'de etkinleşir"
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

          {productsQuery.data!.totalPages > 1 && (
            <Pagination className="justify-content-center">
              <Pagination.Prev
                disabled={page <= 1}
                onClick={() => goToPage(page - 1)}
              />
              {Array.from({ length: productsQuery.data!.totalPages }, (_, i) => i + 1).map(
                (n) => (
                  <Pagination.Item
                    key={n}
                    active={n === page}
                    onClick={() => goToPage(n)}
                  >
                    {n}
                  </Pagination.Item>
                ),
              )}
              <Pagination.Next
                disabled={page >= productsQuery.data!.totalPages}
                onClick={() => goToPage(page + 1)}
              />
            </Pagination>
          )}
        </>
      )}
    </>
  )
}
