import { Link } from 'react-router'

export default function NotFound() {
  return (
    <div className="text-center py-5">
      <h2>Sayfa bulunamadı</h2>
      <p className="text-muted">Aradığınız sayfa mevcut değil.</p>
      <Link to="/" className="btn btn-primary">
        Ana sayfaya dön
      </Link>
    </div>
  )
}
