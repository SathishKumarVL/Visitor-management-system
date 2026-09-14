import { create } from 'zustand'
import {
  authApi,
  clearToken,
  getStoredRefreshToken,
  getStoredToken,
  storeAuthTokens,
} from '../lib/api'
import type { ChangePasswordRequest, UserDto } from '../types/api'
import { homePathForRoles } from '../lib/utils'

interface AuthState {
  user: UserDto | null
  token: string | null
  refreshToken: string | null
  loading: boolean
  initialized: boolean
  login: (username: string, password: string, rememberMe: boolean) => Promise<string>
  logout: () => Promise<void>
  bootstrap: () => Promise<void>
  setUser: (user: UserDto | null) => void
  changePassword: (body: ChangePasswordRequest) => Promise<void>
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  token: getStoredToken(),
  refreshToken: getStoredRefreshToken(),
  loading: false,
  initialized: false,

  setUser: (user) => set({ user }),

  login: async (username, password, rememberMe) => {
    set({ loading: true })
    try {
      const result = await authApi.login(username, password, rememberMe)
      storeAuthTokens(result.token, result.refreshToken, rememberMe)
      set({
        token: result.token,
        refreshToken: result.refreshToken ?? null,
        user: result.user,
        loading: false,
      })
      if (result.user.mustChangePassword) return '/change-password'
      return homePathForRoles(result.user.roles)
    } catch (e) {
      set({ loading: false })
      throw e
    }
  },

  changePassword: async (body) => {
    await authApi.changePassword(body)
    const user = get().user
    if (user) set({ user: { ...user, mustChangePassword: false } })
  },

  logout: async () => {
    try {
      if (get().token) await authApi.logout()
    } catch {
      /* ignore */
    }
    clearToken()
    set({ user: null, token: null, refreshToken: null })
  },

  bootstrap: async () => {
    const token = getStoredToken()
    if (!token) {
      set({ initialized: true, user: null, token: null, refreshToken: null })
      return
    }
    set({ loading: true, token, refreshToken: getStoredRefreshToken() })
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
      set({ user: null, token: null, refreshToken: null, loading: false, initialized: true })
    }
  },
}))
