import { Badge, Table } from 'react-bootstrap'
import { Link } from 'react-router'
import type { ProductListDto } from '../types/api'
import { formatCurrency } from '../lib/format'

// Read-only product table shared by the Tedarikçi and Kategori detail pages. The one
// varying column is the "other" dimension: on a supplier page every product shares the
// supplier, so we show Kategori; on a category page we show Tedarikçi. Product name links
// to the product detail page.
export function ProductMiniTable({
  items,
  otherColumn,
}: {
  items: ProductListDto[]
  otherColumn: 'category' | 'supplier'
}) {
  return (
    <Table hover responsive className="align-middle">
      <thead>
        <tr>
          <th>Ürün</th>
          <th>SKU</th>
          <th>{otherColumn === 'category' ? 'Kategori' : 'Tedarikçi'}</th>
          <th className="text-end">Fiyat</th>
          <th className="text-end">Stok</th>
          <th>Durum</th>
        </tr>
      </thead>
      <tbody>
        {items.length === 0 ? (
          <tr>
            <td colSpan={6} className="text-center text-muted py-4">
              Ürün yok.
            </td>
          </tr>
        ) : (
          items.map((p) => {
            const low = p.stockQuantity <= p.minStockLevel
            return (
              <tr key={p.id} className={p.isActive ? undefined : 'table-secondary'}>
                <td>
                  <Link to={`/urunler/${p.id}`} className="text-decoration-none">
                    {p.name}
                  </Link>
                </td>
                <td className="text-muted">{p.sku}</td>
                <td className="text-muted">
                  {otherColumn === 'category' ? p.categoryName : p.supplierName}
                </td>
                <td className="text-end">{formatCurrency(p.unitPrice)}</td>
                <td className="text-end">
                  {low ? (
                    <Badge bg="danger">{p.stockQuantity} · Düşük</Badge>
                  ) : (
                    p.stockQuantity
                  )}
                </td>
                <td>
                  <Badge bg={p.isActive ? 'success' : 'secondary'}>
                    {p.isActive ? 'Aktif' : 'Pasif'}
                  </Badge>
                </td>
              </tr>
            )
          })
        )}
      </tbody>
    </Table>
  )
}
