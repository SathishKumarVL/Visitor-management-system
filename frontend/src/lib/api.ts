import axios, { type AxiosError } from 'axios'
import type {
  ApiResponse,
  CreateEmployeeRequest,
  CreateUserRequest,
  DashboardDto,
  EmployeeDto,
  ExpectedVisitorRequest,
  LoginResponse,
  MasterItemDto,
  MasterUpsertRequest,
  PagedResult,
  PassDto,
  RegisterVisitorRequest,
  SettingsDto,
  UpdateUserRequest,
  UserDto,
  VisitorDetailDto,
  VisitorListItemDto,
  VisitorSearchParams,
  AuditLogDto,
} from '../types/api'

const TOKEN_KEY = 'tiaano_vms_token'

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY)
}

export function storeToken(token: string, rememberMe: boolean) {
  localStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(TOKEN_KEY)
  const store = rememberMe ? localStorage : sessionStorage
  store.setItem(TOKEN_KEY, token)
}

export function clearToken() {
  localStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(TOKEN_KEY)
}

export const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  timeout: 20000,
})

api.interceptors.request.use((config) => {
  const token = getStoredToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

api.interceptors.response.use(
  (res) => res,
  (error: AxiosError<ApiResponse<unknown>>) => {
    const status = error.response?.status
    const url = error.config?.url ?? ''
    const isLoginCall = url.includes('/auth/login')
    if (status === 401 && !isLoginCall && getStoredToken()) {
      clearToken()
      if (!window.location.pathname.startsWith('/login')) {
        window.location.assign(`/login?redirect=${encodeURIComponent(window.location.pathname)}`)
      }
    }
    return Promise.reject(error)
  },
)

export function apiErrorMessage(error: unknown, fallback = 'Request failed'): string {
  if (axios.isAxiosError(error)) {
    if (error.code === 'ECONNABORTED' || error.message?.toLowerCase().includes('timeout')) {
      return 'The server took too long to respond. Please try again.'
    }
    const data = error.response?.data as ApiResponse<unknown> | undefined
    if (data?.message) return data.message
    if (data?.errors?.length) return data.errors.join(', ')
    if (error.message) return error.message
  }
  if (error instanceof Error) return error.message
  return fallback
}

async function unwrap<T>(promise: Promise<{ data: ApiResponse<T> }>): Promise<T> {
  const { data } = await promise
  if (!data.success || data.data === null || data.data === undefined) {
    throw new Error(data.message || data.errors?.join(', ') || 'Request failed')
  }
  return data.data
}

export const authApi = {
  login: (username: string, password: string, rememberMe: boolean) =>
    unwrap(api.post<ApiResponse<LoginResponse>>('/auth/login', { username, password, rememberMe })),
  me: () => unwrap(api.get<ApiResponse<UserDto>>('/auth/me')),
  logout: () => api.post<ApiResponse<object>>('/auth/logout'),
}

export const visitorsApi = {
  search: (params: VisitorSearchParams) =>
    unwrap(api.get<ApiResponse<PagedResult<VisitorListItemDto>>>('/visitors', { params })),
  get: (id: string) => unwrap(api.get<ApiResponse<VisitorDetailDto>>(`/visitors/${id}`)),
  register: (body: RegisterVisitorRequest) =>
    unwrap(api.post<ApiResponse<VisitorDetailDto>>('/visitors', body)),
  inside: () => unwrap(api.get<ApiResponse<VisitorListItemDto[]>>('/visitors/inside')),
  expected: (date?: string) =>
    unwrap(api.get<ApiResponse<VisitorListItemDto[]>>('/visitors/expected', { params: { date } })),
  createExpected: (body: ExpectedVisitorRequest) =>
    unwrap(api.post<ApiResponse<VisitorDetailDto>>('/visitors/expected', body)),
  checkIn: (id: string, entryGateId?: string) =>
    unwrap(api.post<ApiResponse<PassDto>>(`/visitors/${id}/check-in`, { entryGateId })),
  checkOut: (id: string, exitGateId?: string) =>
    unwrap(api.post<ApiResponse<VisitorListItemDto>>(`/visitors/${id}/check-out`, { exitGateId })),
}

export const approvalsApi = {
  list: () => unwrap(api.get<ApiResponse<VisitorListItemDto[]>>('/approvals')),
  approve: (id: string) => unwrap(api.post<ApiResponse<VisitorListItemDto>>(`/approvals/${id}/approve`)),
  reject: (id: string, reason: string) =>
    unwrap(api.post<ApiResponse<VisitorListItemDto>>(`/approvals/${id}/reject`, { reason })),
}

export const passApi = {
  get: (visitId: string) => unwrap(api.get<ApiResponse<PassDto>>(`/pass/${visitId}`)),
  generate: (visitId: string) => unwrap(api.post<ApiResponse<PassDto>>(`/pass/${visitId}/generate`)),
  verify: (visitNumber: string) =>
    unwrap(api.get<ApiResponse<PassDto>>(`/pass/verify/${encodeURIComponent(visitNumber)}`)),
}

export const dashboardApi = {
  get: () => unwrap(api.get<ApiResponse<DashboardDto>>('/dashboard')),
}

export const mastersApi = {
  departments: (activeOnly = true) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/departments', { params: { activeOnly } })),
  createDepartment: (body: MasterUpsertRequest) =>
    unwrap(api.post<ApiResponse<MasterItemDto>>('/masters/departments', body)),
  updateDepartment: (id: string, body: MasterUpsertRequest) =>
    unwrap(api.put<ApiResponse<MasterItemDto>>(`/masters/departments/${id}`, body)),
  hosts: (departmentId?: string, activeOnly = true) =>
    unwrap(api.get<ApiResponse<EmployeeDto[]>>('/masters/hosts', { params: { departmentId, activeOnly } })),
  createHost: (body: CreateEmployeeRequest) =>
    unwrap(api.post<ApiResponse<EmployeeDto>>('/masters/hosts', body)),
  updateHost: (id: string, body: CreateEmployeeRequest) =>
    unwrap(api.put<ApiResponse<EmployeeDto>>(`/masters/hosts/${id}`, body)),
  purposes: (activeOnly = true) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/purposes', { params: { activeOnly } })),
  createPurpose: (body: MasterUpsertRequest) =>
    unwrap(api.post<ApiResponse<MasterItemDto>>('/masters/purposes', body)),
  updatePurpose: (id: string, body: MasterUpsertRequest) =>
    unwrap(api.put<ApiResponse<MasterItemDto>>(`/masters/purposes/${id}`, body)),
  locations: (activeOnly = true) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/locations', { params: { activeOnly } })),
  createLocation: (body: MasterUpsertRequest) =>
    unwrap(api.post<ApiResponse<MasterItemDto>>('/masters/locations', body)),
  updateLocation: (id: string, body: MasterUpsertRequest) =>
    unwrap(api.put<ApiResponse<MasterItemDto>>(`/masters/locations/${id}`, body)),
  idTypes: (activeOnly = true) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/id-types', { params: { activeOnly } })),
  entryGates: (activeOnly = true) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/entry-gates', { params: { activeOnly } })),
  exitGates: (activeOnly = true) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/exit-gates', { params: { activeOnly } })),
  deactivate: (type: string, id: string) =>
    unwrap(api.post<ApiResponse<object>>(`/masters/${type}/${id}/deactivate`)),
}

export const settingsApi = {
  get: () => unwrap(api.get<ApiResponse<SettingsDto>>('/settings')),
  update: (body: SettingsDto) => unwrap(api.put<ApiResponse<SettingsDto>>('/settings', body)),
}

export const usersApi = {
  list: () => unwrap(api.get<ApiResponse<UserDto[]>>('/users')),
  create: (body: CreateUserRequest) => unwrap(api.post<ApiResponse<UserDto>>('/users', body)),
  update: (id: string, body: UpdateUserRequest) =>
    unwrap(api.put<ApiResponse<UserDto>>(`/users/${id}`, body)),
}

export const reportsApi = {
  visitorsJson: (params: Record<string, string | number | undefined>) =>
    unwrap(api.get<ApiResponse<unknown>>('/reports/visitors', { params: { ...params, format: 'json' } })),
  exportFile: async (format: 'excel' | 'pdf' | 'csv', params: Record<string, string | number | undefined>) => {
    const res = await api.get('/reports/visitors', {
      params: { ...params, format },
      responseType: 'blob',
    })
    return res.data as Blob
  },
}

export const auditApi = {
  list: (page = 1, pageSize = 50, action?: string) =>
    unwrap(
      api.get<ApiResponse<PagedResult<AuditLogDto>>>('/audit', {
        params: { page, pageSize, action },
      }),
    ),
}
