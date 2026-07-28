import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Form, ListGroup, Spinner } from 'react-bootstrap'
import { getProduct, getProducts } from '../api/products'

const RESULT_LIMIT = 20

/**
 * Picks one product by searching on the server: the API answers with the matches for a
 * term instead of the whole catalogue, so the control keeps working however many products
 * exist. The selected product is read through its own query, so invalidating that product
 * refreshes the stock figure shown next to it.
 */
export function ProductPicker({
  value,
  onChange,
  isInvalid,
}: {
  value: number | null
  onChange: (productId: number | null) => void
  isInvalid?: boolean
}) {
  const [term, setTerm] = useState('')
  const [debouncedTerm, setDebouncedTerm] = useState('')

  useEffect(() => {
    const t = setTimeout(() => setDebouncedTerm(term), 300)
    return () => clearTimeout(t)
  }, [term])

  const selectedQuery = useQuery({
    queryKey: ['product', value],
    queryFn: () => getProduct(value!),
    enabled: value !== null,
  })

  // Only searches once something has been typed: an empty box means no results, not the
  // whole catalogue.
  //
  // Passive products stay searchable even though the backend rejects movements on them.
  // Hiding them reads as "no such product" and leaves the user stuck;
  // showing them with the "(Pasif)" marker, then failing with "activate it first", tells
  // the user what is actually wrong and what to do about it.
  const hasTerm = debouncedTerm.trim().length > 0
  const resultsQuery = useQuery({
    queryKey: ['products', 'picker', debouncedTerm],
    queryFn: () =>
      getProducts({
        search: debouncedTerm.trim(),
        includeInactive: true,
        pageSize: RESULT_LIMIT,
      }),
    enabled: value === null && hasTerm,
  })

  if (value !== null) {
    const product = selectedQuery.data
    return (
      <div className="d-flex align-items-center justify-content-between gap-2 border rounded px-3 py-2">
        {product ? (
          <span>
            <strong>{product.name}</strong>{' '}
            <span className="text-muted">({product.sku})</span>
            {!product.isActive && <span className="text-muted"> (Pasif)</span>}
            <span className="ms-3">
              Stok: <strong className="tnum">{product.stockQuantity}</strong>
            </span>
          </span>
        ) : (
          <Spinner animation="border" size="sm" />
        )}
        <Button
          variant="outline-secondary"
          size="sm"
          onClick={() => {
            onChange(null)
            setTerm('')
          }}
        >
          Değiştir
        </Button>
      </div>
    )
  }

  const items = resultsQuery.data?.items ?? []
  const totalCount = resultsQuery.data?.totalCount ?? 0

  return (
    <>
      <Form.Control
        type="search"
        placeholder="Ürün adı veya SKU ile arayın…"
        value={term}
        onChange={(e) => setTerm(e.target.value)}
        isInvalid={isInvalid}
        autoComplete="off"
      />

      {!hasTerm ? (
        <div className="text-muted small mt-2">
          Ürünü bulmak için ad veya SKU yazmaya başlayın.
        </div>
      ) : /* isLoading, not isFetching: a new search term is a new query key and still shows
             the spinner, but a realtime background refetch must not blank out results the
             user is reading. */
      resultsQuery.isLoading ? (
        <div className="text-center py-3">
          <Spinner animation="border" size="sm" />
        </div>
      ) : items.length === 0 ? (
        <div className="text-muted small mt-2">Eşleşen ürün yok.</div>
      ) : (
        <>
          <ListGroup className="mt-2">
            {items.map((p) => (
              <ListGroup.Item
                key={p.id}
                action
                as="button"
                type="button"
                onClick={() => onChange(p.id)}
              >
                <div className="d-flex justify-content-between gap-3">
                  <span>
                    {p.name} <span className="text-muted">({p.sku})</span>
                    {!p.isActive && <span className="text-muted"> (Pasif)</span>}
                  </span>
                  <span className="text-muted tnum">Stok: {p.stockQuantity}</span>
                </div>
              </ListGroup.Item>
            ))}
          </ListGroup>
          {totalCount > items.length && (
            <div className="text-muted small mt-2">
              {totalCount} eşleşmeden ilk {items.length} tanesi gösteriliyor — aramayı daraltın.
            </div>
          )}
        </>
      )}
    </>
  )
}
