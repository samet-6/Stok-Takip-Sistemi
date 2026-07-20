import { createBrowserRouter, Navigate } from 'react-router'
import { Layout } from './components/Layout'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AdminRoute } from './components/AdminRoute'
import { AnonymousRoute } from './components/AnonymousRoute'
import Login from './pages/Login'
import Register from './pages/Register'
import Products from './pages/Products'
import ProductDetail from './pages/ProductDetail'
import ProductForm from './pages/ProductForm'
import Categories from './pages/Categories'
import Suppliers from './pages/Suppliers'
import StockMovement from './pages/StockMovement'

export const router = createBrowserRouter([
  {
    element: <AnonymousRoute />,
    children: [
      { path: '/login', element: <Login /> },
      { path: '/register', element: <Register /> },
    ],
  },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <Layout />,
        children: [
          { index: true, element: <Products /> },
          { path: 'urunler/:id', element: <ProductDetail /> },
          {
            element: <AdminRoute />,
            children: [
              { path: 'urunler/yeni', element: <ProductForm /> },
              { path: 'urunler/:id/duzenle', element: <ProductForm /> },
              { path: 'kategoriler', element: <Categories /> },
              { path: 'tedarikciler', element: <Suppliers /> },
              { path: 'stok-hareketi', element: <StockMovement /> },
            ],
          },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
])
