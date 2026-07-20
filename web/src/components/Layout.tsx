import { Link, Outlet, useNavigate } from 'react-router'
import { Button, Container, Nav, Navbar } from 'react-bootstrap'
import { useAuthStore, useIsAdmin } from '../stores/authStore'

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
      <Navbar bg="dark" variant="dark" expand="md" className="mb-4">
        <Container>
          <Navbar.Brand as={Link} to="/">
            StokTakip
          </Navbar.Brand>
          <Navbar.Toggle aria-controls="main-nav" />
          <Navbar.Collapse id="main-nav">
            <Nav className="me-auto">
              <Nav.Link as={Link} to="/">
                Ürünler
              </Nav.Link>
              {isAdmin && (
                <>
                  <Nav.Link as={Link} to="/kategoriler">
                    Kategoriler
                  </Nav.Link>
                  <Nav.Link as={Link} to="/tedarikciler">
                    Tedarikçiler
                  </Nav.Link>
                  <Nav.Link as={Link} to="/stok-hareketi">
                    Stok Hareketi
                  </Nav.Link>
                </>
              )}
            </Nav>
            <Nav className="align-items-md-center">
              <Navbar.Text className="me-3">{user?.fullName}</Navbar.Text>
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
