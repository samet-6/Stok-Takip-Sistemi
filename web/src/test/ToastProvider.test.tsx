import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useContext, useState } from 'react'
import { ToastProvider } from '../components/ToastProvider'
import { ToastContext, useToast, type ToastApi } from '../components/toastContext'

function Trigger({ onPress }: { readonly onPress: (api: ToastApi) => void }) {
  const api = useToast()
  return <button onClick={() => onPress(api)}>bas</button>
}

describe('ToastProvider', () => {
  // The provider sits at the top of the app, so every component that reads the toast context
  // re-renders whenever this value changes identity. An object literal in the JSX would hand
  // out a new one on every render of the tree above it.
  it('üstündeki ağaç yeniden render olduğunda context değerinin kimliğini koruyor', () => {
    const seen: (ToastApi | null)[] = []
    let rerenderParent = () => {}

    function Probe() {
      seen.push(useContext(ToastContext))
      return null
    }

    function Host() {
      const [tick, setTick] = useState(0)
      rerenderParent = () => setTick((t) => t + 1)

      return (
        <ToastProvider>
          <Probe />
          <span>{tick}</span>
        </ToastProvider>
      )
    }

    render(<Host />)
    const renderCountBefore = seen.length

    act(() => {
      rerenderParent()
    })

    expect(seen.length).toBeGreaterThan(renderCountBefore)
    expect(seen.at(-1)).toBe(seen[0])
  })

  it('showSuccess çağrısı mesajı ekrana basıyor', async () => {
    render(
      <ToastProvider>
        <Trigger onPress={(api) => api.showSuccess('Kaydedildi')} />
      </ToastProvider>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'bas' }))

    expect(screen.getByText('Kaydedildi')).toBeInTheDocument()
  })

  // One action can report two failures, and both belong on screen. This pins the list
  // rendering, not the id scheme: ids only become observable if two of them collide, and
  // two toasts that collide are also created together and expire together, so nothing a
  // user or a test can see tells them apart.
  it('aynı anda eklenen iki bildirim iki ayrı satır olarak duruyor', async () => {
    render(
      <ToastProvider>
        <Trigger
          onPress={(api) => {
            api.showError('Kayıt başarısız')
            api.showError('Kayıt başarısız')
          }}
        />
      </ToastProvider>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'bas' }))

    expect(screen.getAllByText('Kayıt başarısız')).toHaveLength(2)
  })
})
