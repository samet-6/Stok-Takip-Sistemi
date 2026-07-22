import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Alert, Breadcrumb, Button, Card, Spinner } from 'react-bootstrap'
import { getCategories } from '../api/categories'
import { CatalogDetailView } from '../components/CatalogDetailView'
import { CategoryFormModal } from '../components/CategoryFormModal'
import { useIsAdmin } from '../stores/authStore'

// Category detail, readable by any authenticated user (Admin + Çalışan). The edit
// affordance is admin-only; the tabbed products/movements body is the shared
// CatalogDetailView (scope = categoryId).
export default function KategoriDetay() {
  const { id = '' } = useParams()
  const categoryId = Number(id)
  const isAdmin = useIsAdmin()
  const [showEdit, setShowEdit] = useState(false)

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: getCategories })
  const category = categoriesQuery.data?.find((c) => c.id === categoryId)

  if (categoriesQuery.isLoading) {
    return (
      <div className="text-center py-5">
        <Spinner animation="border" />
      </div>
    )
  }

  if (!category) {
    return (
      <Alert variant="warning">
        Kategori bulunamadı. <Link to="/kategoriler">Kategoriler listesine dön</Link>.
      </Alert>
    )
  }

  return (
    <>
      <Breadcrumb>
        <Breadcrumb.Item linkAs={Link} linkProps={{ to: '/kategoriler' }}>
          Kategoriler
        </Breadcrumb.Item>
        <Breadcrumb.Item active>{category.name}</Breadcrumb.Item>
      </Breadcrumb>

      <Card className="mb-4">
        <Card.Body>
          <div className="d-flex justify-content-between align-items-start mb-2">
            <h2 className="mb-0">{category.name}</h2>
            {isAdmin && (
              <Button variant="outline-secondary" size="sm" onClick={() => setShowEdit(true)}>
                Düzenle
              </Button>
            )}
          </div>
          <div className="text-muted">{category.description || '—'}</div>
        </Card.Body>
      </Card>

      <CatalogDetailView scope={{ categoryId }} otherColumn="supplier" />

      <CategoryFormModal show={showEdit} category={category} onHide={() => setShowEdit(false)} />
    </>
  )
}
