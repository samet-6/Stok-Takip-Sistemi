import { createBrowserRouter } from 'react-router'
import { Layout } from './components/Layout'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AdminRoute } from './components/AdminRoute'
import { AnonymousRoute } from './components/AnonymousRoute'
import Login from './pages/Login'
import Products from './pages/Products'
import ProductDetail from './pages/ProductDetail'
import ProductForm from './pages/ProductForm'
import Categories from './pages/Categories'
import KategoriDetay from './pages/KategoriDetay'
import Suppliers from './pages/Suppliers'
import TedarikciDetay from './pages/TedarikciDetay'
import StockMovement from './pages/StockMovement'
import Calisanlar from './pages/Calisanlar'
import CalisanHareketleri from './pages/CalisanHareketleri'
import Hesabim from './pages/Hesabim'
import NotFound from './pages/NotFound'

export const router = createBrowserRouter([
  {
    element: <AnonymousRoute />,
    children: [
      { path: '/login', element: <Login /> },
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
          // Operational + self-service: any authenticated user (Admin + Çalışan).
          { path: 'stok-hareketi', element: <StockMovement /> },
          { path: 'hesabim', element: <Hesabim /> },
          {
            element: <AdminRoute />,
            children: [
              { path: 'urunler/yeni', element: <ProductForm /> },
              { path: 'urunler/:id/duzenle', element: <ProductForm /> },
              { path: 'kategoriler', element: <Categories /> },
              { path: 'kategoriler/:id', element: <KategoriDetay /> },
              { path: 'tedarikciler', element: <Suppliers /> },
              { path: 'tedarikciler/:id', element: <TedarikciDetay /> },
              { path: 'calisanlar', element: <Calisanlar /> },
              { path: 'calisanlar/:id/hareketler', element: <CalisanHareketleri /> },
            ],
          },
          // Authenticated but unknown route → 404 within the app chrome.
          { path: '*', element: <NotFound /> },
        ],
      },
    ],
  },
])
