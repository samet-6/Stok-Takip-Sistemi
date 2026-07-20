import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, UserDto } from '../types/api'

interface AuthState {
  token: string | null
  expiresAt: string | null
  user: UserDto | null
  login: (auth: AuthResponse) => void
  logout: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      expiresAt: null,
      user: null,
      login: (auth) =>
        set({ token: auth.token, expiresAt: auth.expiresAt, user: auth.user }),
      logout: () => set({ token: null, expiresAt: null, user: null }),
    }),
    { name: 'auth-storage' },
  ),
)

// Derived selectors — computed, never stored.
export const selectIsAuthenticated = (s: AuthState) => s.token !== null
export const selectIsAdmin = (s: AuthState) =>
  s.user?.roles.includes('Admin') ?? false

export const useIsAdmin = () => useAuthStore(selectIsAdmin)

/**
 * Reads the persisted token directly (outside React) and clears it when expired.
 * Called once at app startup so a stale token never hydrates the UI as logged-in.
 */
export function purgeExpiredSession(): void {
  const { token, expiresAt, logout } = useAuthStore.getState()
  if (token && expiresAt && new Date(expiresAt).getTime() <= Date.now()) {
    logout()
  }
}
