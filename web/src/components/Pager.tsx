import { Pagination } from 'react-bootstrap'

// Shared numbered pager. Renders nothing for a single page, so call sites don't need their
// own `totalPages > 1` guard.
export function Pager({
  page,
  totalPages,
  onChange,
}: {
  page: number
  totalPages: number
  onChange: (n: number) => void
}) {
  if (totalPages <= 1) return null
  return (
    <Pagination className="justify-content-center">
      <Pagination.Prev disabled={page <= 1} onClick={() => onChange(page - 1)} />
      {Array.from({ length: totalPages }, (_, i) => i + 1).map((n) => (
        <Pagination.Item key={n} active={n === page} onClick={() => onChange(n)}>
          {n}
        </Pagination.Item>
      ))}
      <Pagination.Next disabled={page >= totalPages} onClick={() => onChange(page + 1)} />
    </Pagination>
  )
}
