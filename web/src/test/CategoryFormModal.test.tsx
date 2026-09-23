import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, type AxiosResponse } from 'axios'
import type { ReactNode } from 'react'
import { CategoryFormModal } from '../components/CategoryFormModal'
import { ToastContext } from '../components/toastContext'
import { getCategories, updateCategory } from '../api/categories'
import type { CategoryDto } from '../types/api'

vi.mock('../api/categories', () => ({
  getCategories: vi.fn(),
  createCategory: vi.fn(),
  updateCategory: vi.fn(),
}))

const OPENED: CategoryDto = {
  id: 1,
  name: 'Gıda',
  description: null,
  isActive: true,
  deactivatedAt: null,
  productCount: 3,
  rowVersion: 7,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
}

function axiosFailure(status: number, data: unknown): AxiosError {
  const error = new AxiosError(`Request failed with status code ${status}`)
  error.response = { status, data } as AxiosResponse
  return error
}

function renderModal(category: CategoryDto = OPENED) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const toast = { showSuccess: vi.fn(), showError: vi.fn() }
  const wrap = (node: ReactNode) => (
    <QueryClientProvider client={client}>
      <ToastContext.Provider value={toast}>{node}</ToastContext.Provider>
    </QueryClientProvider>
  )
  const view = render(wrap(<CategoryFormModal show category={category} onHide={vi.fn()} />))
  const rerender = (next: CategoryDto) =>
    view.rerender(wrap(<CategoryFormModal show category={next} onHide={vi.fn()} />))
  return { rerender, toast }
}

async function typeName(value: string) {
  const input = screen.getByLabelText('Ad')
  await userEvent.clear(input)
  await userEvent.type(input, value)
}

const save = () => userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

describe('CategoryFormModal — sürüm çakışması (D10)', () => {
  beforeEach(() => {
    vi.mocked(updateCategory).mockReset()
    vi.mocked(getCategories).mockReset()
  })

  it('açıldığı andaki rowVersion ile kaydediyor', async () => {
    vi.mocked(updateCategory).mockResolvedValue()
    renderModal()

    await save()

    await waitFor(() =>
      expect(updateCategory).toHaveBeenCalledWith(1, expect.objectContaining({ rowVersion: 7 })),
    )
  })

  // The detail page hands the modal live query data; a background refetch used to reset the form,
  // wiping what was typed and quietly adopting the new version — the lost update D10 is about.
  it('açıkken arkadan gelen yeni veri yazılanı silmiyor ve sürümü değiştirmiyor', async () => {
    vi.mocked(updateCategory).mockResolvedValue()
    const { rerender } = renderModal()
    await typeName('Yazılan')

    rerender({ ...OPENED, name: 'Başkası', rowVersion: 8 })
    await save()

    expect(screen.getByLabelText('Ad')).toHaveValue('Yazılan')
    await waitFor(() =>
      expect(updateCategory).toHaveBeenCalledWith(1, expect.objectContaining({ rowVersion: 7 })),
    )
  })

  it('sürüm çakışmasında yazılanlar kalıyor ve uyarı çıkıyor', async () => {
    vi.mocked(updateCategory).mockRejectedValue(
      axiosFailure(409, { status: 409, title: 'Çakışma', code: 'concurrency_conflict' }),
    )
    renderModal()
    await typeName('Yazılan')

    await save()

    expect(await screen.findByText(/siz düzenlerken başka bir yerden değiştirildi/)).toBeInTheDocument()
    expect(screen.getByLabelText('Ad')).toHaveValue('Yazılan')
    // A version clash is not a name clash: the name field must not be blamed for it.
    expect(screen.getByLabelText('Ad')).not.toHaveClass('is-invalid')
  })

  it('"Güncel hâlini yükle" taze kaydı getiriyor ve sonraki kayıt yeni sürümle gidiyor', async () => {
    vi.mocked(updateCategory)
      .mockRejectedValueOnce(
        axiosFailure(409, { status: 409, title: 'Çakışma', code: 'concurrency_conflict' }),
      )
      .mockResolvedValueOnce()
    vi.mocked(getCategories).mockResolvedValue([{ ...OPENED, name: 'Taze', rowVersion: 9 }])
    renderModal()
    await typeName('Yazılan')
    await save()

    await userEvent.click(await screen.findByRole('button', { name: 'Güncel hâlini yükle' }))

    await waitFor(() => expect(screen.getByLabelText('Ad')).toHaveValue('Taze'))
    expect(screen.queryByText(/siz düzenlerken başka bir yerden değiştirildi/)).not.toBeInTheDocument()

    await save()
    await waitFor(() =>
      expect(updateCategory).toHaveBeenLastCalledWith(1, expect.objectContaining({ rowVersion: 9 })),
    )
  })

  // Guards the other 409 while the conflict branch is added next to it.
  it('ad çakışması 409 u hâlâ ad alanına yazılıyor', async () => {
    vi.mocked(updateCategory).mockRejectedValue(
      axiosFailure(409, { status: 409, title: 'Bu kategori adı zaten kayıtlı' }),
    )
    renderModal()

    await save()

    expect(await screen.findByText('Bu kategori adı zaten kayıtlı')).toBeInTheDocument()
    expect(screen.getByLabelText('Ad')).toHaveClass('is-invalid')
  })
})
