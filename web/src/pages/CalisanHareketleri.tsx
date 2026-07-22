import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Alert, Spinner } from 'react-bootstrap'
import { getStockMovements } from '../api/stockMovements'
import { getUsers } from '../api/users'
import { MovementsTable } from '../components/MovementsTable'
import { Pager } from '../components/Pager'

// Admin-only drilldown: one employee's stock movements (read-only, with "Yapan").
export default function CalisanHareketleri() {
  const { id = '' } = useParams()
  const [page, setPage] = useState(1)

  // Name for the heading — reused from the cached Çalışanlar list (admin arrives here
  // by clicking a name there, so it's already loaded).
  const usersQuery = useQuery({ queryKey: ['users'], queryFn: getUsers })
  const employee = usersQuery.data?.find((u) => u.id === id)

  const movementsQuery = useQuery({
    queryKey: ['movements', 'by-user', id, page],
    queryFn: () => getStockMovements({ userId: id, page, pageSize: 10 }),
  })

  const data = movementsQuery.data

  return (
    <>
      <div className="mb-4">
        <Link to="/calisanlar" className="text-decoration-none small">
          ← Çalışanlar
        </Link>
        <h2 className="mb-0 mt-1">
          {employee ? `${employee.fullName} — Hareketleri` : 'Çalışan Hareketleri'}
        </h2>
      </div>

      {movementsQuery.isLoading ? (
        <div className="text-center py-5">
          <Spinner animation="border" />
        </div>
      ) : movementsQuery.isError ? (
        <Alert variant="danger">Hareketler yüklenemedi.</Alert>
      ) : !data || data.items.length === 0 ? (
        <Alert variant="secondary">Bu çalışanın henüz hareketi yok.</Alert>
      ) : (
        <>
          <MovementsTable items={data.items} showCreatedBy />
          <Pager page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      )}
    </>
  )
}
