import { Table } from 'react-bootstrap'
import type { StockMovementDto } from '../types/api'
import { formatDateTime } from '../lib/format'
import { StatusChip } from './StatusChip'

// Read-only stock movement list, reused by Hesabım (own movements) and the admin
// per-employee drilldown. The "Yapan" (created-by) column is admin-only — a
// Çalışan's own list doesn't need it (every row is theirs).
export function MovementsTable({
  items,
  showCreatedBy = false,
}: {
  items: StockMovementDto[]
  showCreatedBy?: boolean
}) {
  return (
    <div className="table-card">
      <Table hover responsive className="align-middle">
        <thead>
          <tr>
            <th>Ürün</th>
            <th>Tip</th>
            <th className="text-end">Miktar</th>
            <th>Not</th>
            <th>Tarih</th>
            {showCreatedBy && <th>Yapan</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((m) => (
            <tr key={m.id}>
              <td>{m.productName}</td>
              <td>
                <StatusChip variant={m.type === 'In' ? 'ok' : 'crit'}>
                  {m.type === 'In' ? 'Giriş' : 'Çıkış'}
                </StatusChip>
              </td>
              <td className="text-end">{m.quantity}</td>
              <td className="text-muted">{m.note}</td>
              <td>{formatDateTime(m.createdAt)}</td>
              {showCreatedBy && <td>{m.createdByFullName}</td>}
            </tr>
          ))}
        </tbody>
      </Table>
    </div>
  )
}
