import {
  purgeExpiredSession,
  selectIsAdmin,
  selectIsAuthenticated,
  useAuthStore,
} from '../stores/authStore'
import type { AuthResponse, UserDto } from '../types/api'

const ADMIN: UserDto = {
  id: '1',
  email: 'admin@stok.local',
  fullName: 'Yönetici',
  roles: ['Admin'],
}

const CALISAN: UserDto = { ...ADMIN, id: '2', fullName: 'Çalışan', roles: ['Calisan'] }

function session(expiresAt: Date, user: UserDto = ADMIN): AuthResponse {
  return { token: 'jwt-token', expiresAt: expiresAt.toISOString(), user }
}

const hoursFromNow = (hours: number) => new Date(Date.now() + hours * 3_600_000)

// The store persists to localStorage, so state survives between tests unless cleared.
beforeEach(() => {
  useAuthStore.setState({ token: null, expiresAt: null, user: null })
  localStorage.clear()
})

describe('oturum durumu', () => {
  it('login oturumu yazıyor', () => {
    useAuthStore.getState().login(session(hoursFromNow(8)))

    const state = useAuthStore.getState()
    expect(state.token).toBe('jwt-token')
    expect(state.user).toEqual(ADMIN)
    expect(selectIsAuthenticated(state)).toBe(true)
  })

  it('logout üç alanı da temizliyor', () => {
    useAuthStore.getState().login(session(hoursFromNow(8)))
    useAuthStore.getState().logout()

    const state = useAuthStore.getState()
    expect(state.token).toBeNull()
    expect(state.expiresAt).toBeNull()
    expect(state.user).toBeNull()
    expect(selectIsAuthenticated(state)).toBe(false)
  })
})

describe('purgeExpiredSession', () => {
  // Without this, a token that expired while the tab was closed hydrates the UI as logged-in
  // and the user only finds out when the first request comes back 401.
  it('süresi geçmiş oturumu düşürüyor', () => {
    useAuthStore.getState().login(session(hoursFromNow(-1)))

    purgeExpiredSession()

    expect(useAuthStore.getState().token).toBeNull()
  })

  it('geçerli oturuma dokunmuyor', () => {
    useAuthStore.getState().login(session(hoursFromNow(8)))

    purgeExpiredSession()

    expect(useAuthStore.getState().token).toBe('jwt-token')
  })

  it('oturum yokken hata vermiyor', () => {
    expect(() => purgeExpiredSession()).not.toThrow()
    expect(useAuthStore.getState().token).toBeNull()
  })
})

describe('selectIsAdmin', () => {
  it('Admin rolü varsa true', () => {
    useAuthStore.getState().login(session(hoursFromNow(8), ADMIN))

    expect(selectIsAdmin(useAuthStore.getState())).toBe(true)
  })

  // Catalog write surfaces hang off this selector; a wrong true shows buttons the API
  // will reject with 403.
  it('Çalışan için false', () => {
    useAuthStore.getState().login(session(hoursFromNow(8), CALISAN))

    expect(selectIsAdmin(useAuthStore.getState())).toBe(false)
  })

  it('oturum yokken false', () => {
    expect(selectIsAdmin(useAuthStore.getState())).toBe(false)
  })
})
