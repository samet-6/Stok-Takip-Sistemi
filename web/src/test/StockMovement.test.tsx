import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, type AxiosResponse } from 'axios'
import StockMovement from '../pages/StockMovement'
import { ToastContext } from '../components/toastContext'
import { createStockMovement } from '../api/stockMovements'
import type { StockMovementResponse } from '../types/api'

vi.mock('../api/stockMovements', () => ({
  createStockMovement: vi.fn(),
  getStockMovements: vi.fn(),
}))

// The real picker searches the server; here it only has to hand the form a product id.
vi.mock('../components/ProductPicker', () => ({
  ProductPicker: ({ onChange }: { onChange: (id: number | null) => void }) => (
    <button type="button" onClick={() => onChange(5)}>
      Ürünü seç
    </button>
  ),
}))

const SAVED: StockMovementResponse = {
  movement: {
    id: 1,
    productId: 5,
    productName: 'Kalem',
    productIsActive: true,
    type: 'In',
    quantity: 3,
    note: null,
    createdAt: '2026-09-23T10:00:00Z',
    createdByFullName: 'Admin',
  },
  newStockQuantity: 13,
}

function failure(status?: number, data?: unknown): AxiosError {
  const error = new AxiosError(status ? `Request failed with status code ${status}` : 'Network Error')
  if (status) error.response = { status, data } as AxiosResponse
  return error
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const toast = { showSuccess: vi.fn(), showError: vi.fn() }
  render(
    <QueryClientProvider client={client}>
      <ToastContext.Provider value={toast}>
        <StockMovement />
      </ToastContext.Provider>
    </QueryClientProvider>,
  )
  return toast
}

async function fillAndSubmit(quantity = '3') {
  await userEvent.click(screen.getByRole('button', { name: 'Ürünü seç' }))
  const input = screen.getByLabelText('Miktar')
  await userEvent.clear(input)
  await userEvent.type(input, quantity)
  await userEvent.click(screen.getByRole('button', { name: 'Hareket Ekle' }))
}

const sentKey = (call: number) => vi.mocked(createStockMovement).mock.calls[call][1]

describe('StockMovement — idempotency anahtarı (D27)', () => {
  beforeEach(() => {
    vi.mocked(createStockMovement).mockReset()
  })

  it('her gönderim bir anahtar taşıyor', async () => {
    vi.mocked(createStockMovement).mockResolvedValue(SAVED)
    renderPage()

    await fillAndSubmit()

    await waitFor(() => expect(createStockMovement).toHaveBeenCalledTimes(1))
    expect(sentKey(0)).toMatch(/^[0-9a-f-]{36}$/)
  })

  // The case the key exists for: the answer never arrived, so the user cannot know whether the
  // movement was saved. Sending again must be recognisable as the same movement.
  it('bağlantı hatasından sonra tekrar gönderim aynı anahtarı kullanıyor', async () => {
    vi.mocked(createStockMovement).mockRejectedValueOnce(failure()).mockResolvedValueOnce(SAVED)
    renderPage()

    await fillAndSubmit()
    await waitFor(() => expect(createStockMovement).toHaveBeenCalledTimes(1))
    await userEvent.click(screen.getByRole('button', { name: 'Hareket Ekle' }))

    await waitFor(() => expect(createStockMovement).toHaveBeenCalledTimes(2))
    expect(sentKey(1)).toBe(sentKey(0))
  })

  it('başarılı kayıttan sonraki hareket yeni anahtar alıyor', async () => {
    vi.mocked(createStockMovement).mockResolvedValue(SAVED)
    renderPage()

    await fillAndSubmit()
    await waitFor(() => expect(createStockMovement).toHaveBeenCalledTimes(1))
    await fillAndSubmit('4')

    await waitFor(() => expect(createStockMovement).toHaveBeenCalledTimes(2))
    expect(sentKey(1)).not.toBe(sentKey(0))
  })

  // 422: this key already holds a different movement. The user is told so, and sending again
  // is a new intent — a new key — rather than the same refusal forever.
  it('422 idempotency_key_reused sonrası uyarı veriyor ve yeni anahtara geçiyor', async () => {
    vi.mocked(createStockMovement)
      .mockRejectedValueOnce(
        failure(422, { status: 422, title: 'x', code: 'idempotency_key_reused' }),
      )
      .mockResolvedValueOnce(SAVED)
    const toast = renderPage()

    await fillAndSubmit()
    await waitFor(() =>
      expect(toast.showError).toHaveBeenCalledWith(
        expect.stringContaining('hareket listesini kontrol edin'),
      ),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Hareket Ekle' }))

    await waitFor(() => expect(createStockMovement).toHaveBeenCalledTimes(2))
    expect(sentKey(1)).not.toBe(sentKey(0))
  })
})
