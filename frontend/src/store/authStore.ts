import { create } from 'zustand'
import { authApi, clearToken, getStoredToken, storeToken } from '../lib/api'
import type { UserDto } from '../types/api'
import { homePathForRoles } from '../lib/utils'

interface AuthState {
  user: UserDto | null
  token: string | null
  loading: boolean
  initialized: boolean
  login: (username: string, password: string, rememberMe: boolean) => Promise<string>
  logout: () => Promise<void>
  bootstrap: () => Promise<void>
  setUser: (user: UserDto | null) => void
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  token: getStoredToken(),
  loading: false,
  initialized: false,

  setUser: (user) => set({ user }),

  login: async (username, password, rememberMe) => {
    set({ loading: true })
    try {
      const result = await authApi.login(username, password, rememberMe)
      storeToken(result.token, rememberMe)
      set({ token: result.token, user: result.user, loading: false })
      return homePathForRoles(result.user.roles)
    } catch (e) {
      set({ loading: false })
      throw e
    }
  },

  logout: async () => {
    try {
      if (get().token) await authApi.logout()
    } catch {
      /* ignore */
    }
    clearToken()
    set({ user: null, token: null })
  },

  bootstrap: async () => {
    const token = getStoredToken()
    if (!token) {
      set({ initialized: true, user: null, token: null })
      return
    }
    set({ loading: true, token })
    try {
      const user = await Promise.race([
        authApi.me(),
        new Promise<never>((_, reject) =>
          setTimeout(() => reject(new Error('Auth check timed out')), 8000),
        ),
      ])
      set({ user, loading: false, initialized: true })
    } catch {
      clearToken()
      set({ user: null, token: null, loading: false, initialized: true })
    }
  },
}))
