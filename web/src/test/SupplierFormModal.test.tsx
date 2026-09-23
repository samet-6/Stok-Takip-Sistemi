import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, type AxiosResponse } from 'axios'
import { SupplierFormModal } from '../components/SupplierFormModal'
import { ToastContext } from '../components/toastContext'
import { updateSupplier } from '../api/suppliers'
import type { SupplierDto } from '../types/api'

vi.mock('../api/suppliers', () => ({
  getSuppliers: vi.fn(),
  createSupplier: vi.fn(),
  updateSupplier: vi.fn(),
}))

const OPENED: SupplierDto = {
  id: 2,
  name: 'Ege Kırtasiye',
  contactEmail: 'ege@firma.com',
  phone: null,
  address: null,
  isActive: true,
  deactivatedAt: null,
  productCount: 0,
  rowVersion: 11,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
}

function renderModal() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ToastContext.Provider value={{ showSuccess: vi.fn(), showError: vi.fn() }}>
        <SupplierFormModal show supplier={OPENED} onHide={vi.fn()} />
      </ToastContext.Provider>
    </QueryClientProvider>,
  )
}

const save = () => userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

// The behaviour itself is pinned in CategoryFormModal.test.tsx — both modals share one hook.
// These two only prove the supplier modal is wired to it.
describe('SupplierFormModal — sürüm çakışması (D10)', () => {
  // Braces matter: a function returned from beforeEach is run as teardown.
  beforeEach(() => {
    vi.mocked(updateSupplier).mockReset()
  })

  it('açıldığı andaki rowVersion ile kaydediyor', async () => {
    vi.mocked(updateSupplier).mockResolvedValue()
    renderModal()

    await save()

    await waitFor(() =>
      expect(updateSupplier).toHaveBeenCalledWith(2, expect.objectContaining({ rowVersion: 11 })),
    )
  })

  it('sürüm çakışmasında uyarı çıkıyor, ad alanı suçlanmıyor', async () => {
    const error = new AxiosError('Request failed with status code 409')
    error.response = {
      status: 409,
      data: { status: 409, title: 'Çakışma', code: 'concurrency_conflict' },
    } as AxiosResponse
    vi.mocked(updateSupplier).mockRejectedValue(error)
    renderModal()

    await save()

    expect(await screen.findByText(/siz düzenlerken başka bir yerden değiştirildi/)).toBeInTheDocument()
    expect(screen.getByLabelText('Ad')).not.toHaveClass('is-invalid')
  })
})
