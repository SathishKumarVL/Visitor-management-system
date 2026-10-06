import axios from 'axios'
import { create } from 'zustand'
import {
  authApi,
  clearToken,
  getStoredRefreshToken,
  getStoredToken,
  isRememberMeSession,
  storeAuthTokens,
} from '../lib/api'
import type { ChangePasswordRequest, LoginResponse, UserDto } from '../types/api'
import { homePathForRoles } from '../lib/utils'

interface AuthState {
  user: UserDto | null
  token: string | null
  refreshToken: string | null
  loading: boolean
  initialized: boolean
  login: (username: string, password: string, rememberMe: boolean) => Promise<string>
  faceLogin: (photoBase64: string, rememberMe: boolean) => Promise<string>
  logout: () => Promise<void>
  bootstrap: () => Promise<void>
  setUser: (user: UserDto | null) => void
  changePassword: (body: ChangePasswordRequest) => Promise<void>
}

/** Stores the tokens and returns where the user should land. */
function startSession(
  set: (partial: Partial<AuthState>) => void,
  result: LoginResponse,
  rememberMe: boolean,
): string {
  storeAuthTokens(result.token, result.refreshToken, rememberMe)
  set({
    token: result.token,
    refreshToken: result.refreshToken ?? null,
    user: result.user,
    loading: false,
  })
  if (result.user.mustChangePassword) return '/change-password'
  return homePathForRoles(result.user.roles, result.user.allowedMenuKeys)
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
      return startSession(set, await authApi.login(username, password, rememberMe), rememberMe)
    } catch (e) {
      set({ loading: false })
      throw e
    }
  },

  faceLogin: async (photoBase64, rememberMe) => {
    set({ loading: true })
    try {
      return startSession(set, await authApi.faceLogin(photoBase64, rememberMe), rememberMe)
    } catch (e) {
      set({ loading: false })
      throw e
    }
  },

  changePassword: async (body) => {
    const result = await authApi.changePassword(body)
    const rememberMe = isRememberMeSession()
    storeAuthTokens(result.token, result.refreshToken, rememberMe)
    set({
      token: result.token,
      refreshToken: result.refreshToken ?? null,
      user: result.user,
    })
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
          setTimeout(() => reject(new Error('Auth check timed out')), 15000),
        ),
      ])
      set({ user, loading: false, initialized: true })
    } catch (e) {
      // Only a real rejection ends the session; a slow or dropped mobile connection keeps the tokens
      // so the next reload can sign straight back in.
      const status = axios.isAxiosError(e) ? e.response?.status : undefined
      if (status === 401 || status === 403) clearToken()
      set({ user: null, token: null, refreshToken: null, loading: false, initialized: true })
    }
  },
}))
