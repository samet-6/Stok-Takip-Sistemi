import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Alert, Spinner } from 'react-bootstrap'
import { getStockMovements } from '../api/stockMovements'
import { getUsers } from '../api/users'
import { MovementsTable } from '../components/MovementsTable'
import { Pager } from '../components/Pager'
import { PageHeader } from '../components/PageHeader'

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

  // Early returns rather than a chain of conditionals inside the markup: each state gets its
  // own line, and the next one added does not deepen a ternary.
  const renderMovements = () => {
    if (movementsQuery.isLoading)
      return (
        <div className="text-center py-5">
          <Spinner animation="border" />
        </div>
      )
    if (movementsQuery.isError) return <Alert variant="danger">Hareketler yüklenemedi.</Alert>
    if (!data || data.items.length === 0)
      return <Alert variant="secondary">Bu çalışanın henüz hareketi yok.</Alert>

    return (
      <>
        <MovementsTable items={data.items} showCreatedBy />
        <Pager page={page} totalPages={data.totalPages} onChange={setPage} />
      </>
    )
  }

  return (
    <>
      <div className="mb-2">
        <Link to="/calisanlar" className="breadcrumb-nav text-decoration-none">
          ← Çalışanlar
        </Link>
      </div>
      <PageHeader
        title={employee ? `${employee.fullName} — Hareketleri` : 'Çalışan Hareketleri'}
      />

      {renderMovements()}
    </>
  )
}
