import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { Button, Container, Nav, Navbar } from 'react-bootstrap'
import { useAuthStore, useIsAdmin } from '../stores/authStore'
import { NotificationBell } from './NotificationBell'

export function Layout() {
  const user = useAuthStore((s) => s.user)
  const logout = useAuthStore((s) => s.logout)
  const isAdmin = useIsAdmin()
  const navigate = useNavigate()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  return (
    <>
      <Navbar expand="md" className="app-navbar mb-4" data-bs-theme="dark">
        <Container>
          <Navbar.Brand as={Link} to="/">
            StokTakip
          </Navbar.Brand>
          <Navbar.Toggle aria-controls="main-nav" />
          <Navbar.Collapse id="main-nav">
            <Nav className="me-auto">
              <Nav.Link as={NavLink} to="/" end>
                Ürünler
              </Nav.Link>
              <Nav.Link as={NavLink} to="/stok-hareketi">
                Stok Hareketi
              </Nav.Link>
              <Nav.Link as={NavLink} to="/kategoriler">
                Kategoriler
              </Nav.Link>
              <Nav.Link as={NavLink} to="/tedarikciler">
                Tedarikçiler
              </Nav.Link>
              {isAdmin && (
                <Nav.Link as={NavLink} to="/calisanlar">
                  Çalışanlar
                </Nav.Link>
              )}
            </Nav>
            <Nav className="align-items-md-center">
              {/* Admin only — the endpoints behind it answer 403 to a Çalışan, so rendering it
                  for them would be a button that can only fail. */}
              {isAdmin && <NotificationBell />}
              <Nav.Link as={NavLink} to="/hesabim" className="me-3 text-light">
                {user?.fullName}
              </Nav.Link>
              <Button variant="outline-light" size="sm" onClick={handleLogout}>
                Çıkış
              </Button>
            </Nav>
          </Navbar.Collapse>
        </Container>
      </Navbar>
      <Container>
        <Outlet />
      </Container>
    </>
  )
}
