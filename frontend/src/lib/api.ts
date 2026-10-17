import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import type {
  ApiResponse,
  BookingMatchDto,
  ChangePasswordRequest,
  CreateEmployeeLoginRequest,
  CreatedIntegrationApiKeyDto,
  IntegrationApiKeyDto,
  IntegrationScope,
  DashboardDto,
  MomNotSentByEmployeeDto,
  EmployeeDto,
  EmployeeProfileRequest,
  ExpectedVisitorRequest,
  FaceCheckoutMatchDto,
  FacePhotoValidationDto,
  FaceSearchMatchDto,
  GroupEmployeeMatchDto,
  LoginResponse,
  MyDetailsDto,
  MailContentTemplateDto,
  MailContentUpsertRequest,
  MasterImportKind,
  MasterImportResultDto,
  MasterItemDto,
  MeetingHallDto,
  MeetingHallUpsertRequest,
  MasterUpsertRequest,
  PagedResult,
  PassDto,
  PublicBrandingDto,
  RegisterVisitorRequest,
  SettingsDto,
  UpdateUserRequest,
  UserDto,
  UserFaceLoginDto,
  VisitorPhoneLookupDto,
  VisitorDetailDto,
  SaveMeetingMinutesRequest,
  VisitorListItemDto,
  VisitorSearchParams,
  PassNumberSeriesDto,
  UpsertPassNumberSeriesRequest,
  FeedbackQuestionDto,
  FeedbackQuestionUpsertRequest,
  SubmitVisitFeedbackRequest,
  VisitFeedbackDto,
} from '../types/api'

const TOKEN_KEY = 'tiaano_vms_token'
const REFRESH_KEY = 'tiaano_vms_refresh'

// Some mobile in-app browsers and private modes block or limit Web Storage; never let that break sign-in.
const memoryStore = new Map<string, string>()

function readKey(key: string): string | null {
  for (const store of [() => localStorage, () => sessionStorage]) {
    try {
      const value = store().getItem(key)
      if (value) return value
    } catch {
      /* storage unavailable */
    }
  }
  return memoryStore.get(key) ?? null
}

function removeKey(key: string) {
  memoryStore.delete(key)
  for (const store of [() => localStorage, () => sessionStorage]) {
    try {
      store().removeItem(key)
    } catch {
      /* storage unavailable */
    }
  }
}

function writeKey(key: string, value: string, rememberMe: boolean) {
  removeKey(key)
  const order = rememberMe ? [() => localStorage, () => sessionStorage] : [() => sessionStorage, () => localStorage]
  for (const store of order) {
    try {
      store().setItem(key, value)
      return
    } catch {
      /* try the next store */
    }
  }
  memoryStore.set(key, value)
}

export function getStoredToken(): string | null {
  return readKey(TOKEN_KEY)
}

export function storeToken(token: string, rememberMe: boolean) {
  writeKey(TOKEN_KEY, token, rememberMe)
}

export function getStoredRefreshToken(): string | null {
  return readKey(REFRESH_KEY)
}

export function storeRefreshToken(refreshToken: string, rememberMe: boolean) {
  writeKey(REFRESH_KEY, refreshToken, rememberMe)
}

export function clearRefreshToken() {
  removeKey(REFRESH_KEY)
}

function prefersRememberMeStorage(): boolean {
  try {
    return !!localStorage.getItem(TOKEN_KEY) || !!localStorage.getItem(REFRESH_KEY)
  } catch {
    return false
  }
}

export function isRememberMeSession(): boolean {
  return prefersRememberMeStorage()
}

export function storeAuthTokens(token: string, refreshToken: string | undefined, rememberMe: boolean) {
  storeToken(token, rememberMe)
  if (refreshToken) storeRefreshToken(refreshToken, rememberMe)
  else clearRefreshToken()
}

export function clearToken() {
  removeKey(TOKEN_KEY)
  clearRefreshToken()
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

type RetriableConfig = InternalAxiosRequestConfig & { _retry?: boolean }

let refreshPromise: Promise<string | null> | null = null

async function refreshAccessToken(): Promise<string | null> {
  const refreshToken = getStoredRefreshToken()
  if (!refreshToken) return null

  try {
    const { data } = await axios.post<ApiResponse<LoginResponse>>(
      '/api/auth/refresh',
      { refreshToken },
      { headers: { 'Content-Type': 'application/json' }, timeout: 20000 },
    )
    if (!data.success || !data.data?.token) return null
    storeAuthTokens(data.data.token, data.data.refreshToken ?? refreshToken, prefersRememberMeStorage())
    return data.data.token
  } catch {
    return null
  }
}

function redirectToLogin() {
  clearToken()
  if (!window.location.pathname.startsWith('/login')) {
    window.location.assign(`/login?redirect=${encodeURIComponent(window.location.pathname)}`)
  }
}

api.interceptors.response.use(
  (res) => res,
  async (error: AxiosError<ApiResponse<unknown>>) => {
    const status = error.response?.status
    const original = error.config as RetriableConfig | undefined
    const url = original?.url ?? ''
    const isAuthBootstrap =
      url.includes('/auth/login') ||
      url.includes('/auth/face-login') ||
      url.includes('/auth/refresh') ||
      url.includes('/auth/logout')

    if (status !== 401 || !original || isAuthBootstrap || original._retry) {
      if (status === 401 && !isAuthBootstrap && getStoredToken() && !getStoredRefreshToken()) {
        redirectToLogin()
      }
      return Promise.reject(error)
    }

    if (!getStoredRefreshToken()) {
      redirectToLogin()
      return Promise.reject(error)
    }

    original._retry = true

    if (!refreshPromise) {
      refreshPromise = refreshAccessToken().finally(() => {
        refreshPromise = null
      })
    }

    const newToken = await refreshPromise
    if (!newToken) {
      redirectToLogin()
      return Promise.reject(error)
    }

    original.headers.Authorization = `Bearer ${newToken}`
    return api.request(original)
  },
)

type ErrorBody = {
  message?: string
  title?: string
  errors?: string[] | Record<string, string[] | string>
}

function errorBodyText(body: unknown): string | null {
  if (!body || typeof body !== 'object') return null
  const data = body as ErrorBody
  if (data.message) return data.message
  if (Array.isArray(data.errors) && data.errors.length) return data.errors.join(', ')
  if (data.errors && typeof data.errors === 'object') {
    const parts = Object.values(data.errors).flatMap((v) => (Array.isArray(v) ? v : [v])).filter(Boolean)
    if (parts.length) return parts.join(' ')
  }
  return data.title || null
}

export function apiErrorMessage(error: unknown, fallback = 'Request failed'): string {
  if (axios.isAxiosError(error)) {
    if (error.code === 'ECONNABORTED' || error.message?.toLowerCase().includes('timeout')) {
      return 'The server took too long to respond. Please try again.'
    }
    const status = error.response?.status
    if (!error.response) {
      return 'Cannot reach the server. Check your internet connection and try again.'
    }
    const text = errorBodyText(error.response.data)
    if (status === 409) {
      return text || 'The visit was modified by another user. Refresh the visit and try again.'
    }
    if (text) return text
    if (status === 429) return 'Too many attempts. Please wait a minute and try again.'
    if (status === 400) {
      return 'The server rejected the request (HTTP 400). If this keeps happening on this computer, clear the browser cache and cookies for this site or contact support.'
    }
    if (status && status >= 500) return 'The server had a problem processing this request. Please try again.'
    if (error.message) return error.message
  }
  if (error instanceof Error) return error.message
  return fallback
}

/** True when the API rejected a stale Visit lifecycle update (optimistic concurrency). */
export function isConflictError(error: unknown): boolean {
  return axios.isAxiosError(error) && error.response?.status === 409
}

async function unwrap<T>(promise: Promise<{ data: ApiResponse<T> }>): Promise<T> {
  const { data } = await promise
  if (!data.success || data.data === null || data.data === undefined) {
    throw new Error(data.message || data.errors?.join(', ') || 'Request failed')
  }
  return data.data
}

/** For endpoints where `data: null` is a valid answer (e.g. "nothing configured yet"). */
async function unwrapNullable<T>(promise: Promise<{ data: ApiResponse<T | null> }>): Promise<T | null> {
  const { data } = await promise
  if (!data.success) {
    throw new Error(data.message || data.errors?.join(', ') || 'Request failed')
  }
  return data.data ?? null
}

/** For actions that only report success; resolves with the server message. */
async function unwrapVoid(promise: Promise<{ data: ApiResponse<unknown> }>): Promise<string | undefined> {
  const { data } = await promise
  if (!data.success) {
    throw new Error(data.message || data.errors?.join(', ') || 'Request failed')
  }
  return data.message ?? undefined
}

export const authApi = {
  login: (username: string, password: string, rememberMe: boolean) =>
    unwrap(api.post<ApiResponse<LoginResponse>>('/auth/login', { username, password, rememberMe })),
  faceLogin: (photoBase64: string, rememberMe: boolean) =>
    unwrap(api.post<ApiResponse<LoginResponse>>('/auth/face-login', { photoBase64, rememberMe })),
  faceLoginStatus: () =>
    unwrap(api.get<ApiResponse<{ available: boolean }>>('/auth/face-login/status')),
  me: () => unwrap(api.get<ApiResponse<UserDto>>('/auth/me')),
  myDetails: () => unwrap(api.get<ApiResponse<MyDetailsDto>>('/auth/me/details')),
  logout: () => api.post<ApiResponse<object>>('/auth/logout'),
  changePassword: (body: ChangePasswordRequest) =>
    unwrap(api.post<ApiResponse<LoginResponse>>('/auth/change-password', body)),
  refresh: (refreshToken: string) =>
    unwrap(api.post<ApiResponse<LoginResponse>>('/auth/refresh', { refreshToken })),
  forgotPassword: (usernameOrEmail: string) =>
    unwrapVoid(api.post<ApiResponse<object>>('/auth/forgot-password', { usernameOrEmail })),
  resetPassword: (userId: string, token: string, newPassword: string) =>
    unwrapVoid(api.post<ApiResponse<object>>('/auth/reset-password', { userId, token, newPassword })),
}

export const visitorsApi = {
  search: (params: VisitorSearchParams) =>
    unwrap(api.get<ApiResponse<PagedResult<VisitorListItemDto>>>('/visitors', { params })),
  get: (id: string) => unwrap(api.get<ApiResponse<VisitorDetailDto>>(`/visitors/${id}`)),
  register: (body: RegisterVisitorRequest) =>
    unwrap(api.post<ApiResponse<VisitorDetailDto>>('/visitors', body, { timeout: 60000 })),
  inside: () => unwrap(api.get<ApiResponse<VisitorListItemDto[]>>('/visitors/inside')),
  expected: (date?: string) =>
    unwrap(api.get<ApiResponse<VisitorListItemDto[]>>('/visitors/expected', { params: { date } })),
  lookupByPhone: (phone: string) =>
    unwrapNullable(api.get<ApiResponse<VisitorPhoneLookupDto | null>>('/visitors/lookup', { params: { phone } })),
  matchBookings: (params: { phone?: string; visitorId?: string }) =>
    unwrap(api.get<ApiResponse<BookingMatchDto[]>>('/visitors/expected/match', { params })),
  createExpected: (body: ExpectedVisitorRequest) =>
    unwrap(api.post<ApiResponse<VisitorDetailDto>>('/visitors/expected', body)),
  checkIn: (id: string, photoBase64?: string | null) =>
    unwrap(api.post<ApiResponse<PassDto>>(`/visitors/${id}/check-in`, {
      occurredAt: new Date().toISOString(),
      photoBase64: photoBase64 || null,
    })),
  checkOut: (id: string, options?: { skipFeedback?: boolean }) =>
    unwrap(api.post<ApiResponse<VisitorListItemDto>>(`/visitors/${id}/check-out`, {
      occurredAt: new Date().toISOString(),
      skipFeedback: options?.skipFeedback === true,
    })),
  saveMeetingMinutes: (id: string, body: SaveMeetingMinutesRequest) =>
    unwrap(api.put<ApiResponse<VisitorDetailDto>>(`/visitors/${id}/meeting-minutes`, body)),
  setMomNotRequired: (id: string, notRequired: boolean) =>
    unwrap(api.put<ApiResponse<VisitorDetailDto>>(`/visitors/${id}/mom-not-required`, { notRequired })),
  getFeedback: (id: string) =>
    unwrapNullable(api.get<ApiResponse<VisitFeedbackDto | null>>(`/visitors/${id}/feedback`)),
  submitFeedback: (id: string, body: SubmitVisitFeedbackRequest) =>
    unwrap(api.post<ApiResponse<VisitFeedbackDto>>(`/visitors/${id}/feedback`, body)),
  faceSearch: (photoBase64: string) =>
    unwrapNullable(api.post<ApiResponse<FaceSearchMatchDto | null>>('/visitors/face-search', { photoBase64 })),
  employeeFaceSearch: (photoBase64: string) =>
    unwrapNullable(api.post<ApiResponse<GroupEmployeeMatchDto | null>>('/visitors/employee-face-search', { photoBase64 })),
  faceIdentifyInside: (photoBase64: string) =>
    unwrapNullable(api.post<ApiResponse<FaceCheckoutMatchDto | null>>('/visitors/face-identify-inside', { photoBase64 })),
  /** Exactly-one-face gate before accepting a visitor registration photo. Does not store the image. */
  validatePhoto: (photoBase64: string) =>
    unwrap(api.post<ApiResponse<FacePhotoValidationDto>>('/visitors/validate-photo', { photoBase64 })),
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
  momNotSentByEmployee: () => unwrap(api.get<ApiResponse<MomNotSentByEmployeeDto[]>>('/dashboard/mom-not-sent-by-employee')),
}

/** Employee menu: employee profiles with their face. Admins only see their own location. */
export const employeeMasterApi = {
  list: (locationId?: string, departmentId?: string, activeOnly = false) =>
    unwrap(api.get<ApiResponse<EmployeeDto[]>>('/employee-master', { params: { locationId, departmentId, activeOnly } })),
  create: (body: EmployeeProfileRequest) =>
    unwrap(api.post<ApiResponse<EmployeeDto>>('/employee-master', body)),
  update: (id: string, body: EmployeeProfileRequest) =>
    unwrap(api.put<ApiResponse<EmployeeDto>>(`/employee-master/${id}`, body)),
  deactivate: (id: string) =>
    unwrapVoid(api.post<ApiResponse<object>>(`/employee-master/${id}/deactivate`)),
}

export const mastersApi = {
  departments: (activeOnly = true, locationId?: string) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/departments', { params: { activeOnly, locationId } })),
  createDepartment: (body: MasterUpsertRequest) =>
    unwrap(api.post<ApiResponse<MasterItemDto>>('/masters/departments', body)),
  updateDepartment: (id: string, body: MasterUpsertRequest) =>
    unwrap(api.put<ApiResponse<MasterItemDto>>(`/masters/departments/${id}`, body)),
  hosts: (departmentId?: string, activeOnly = true, locationId?: string) =>
    unwrap(api.get<ApiResponse<EmployeeDto[]>>('/masters/hosts', { params: { departmentId, activeOnly, locationId } })),
  purposes: (activeOnly = true, locationId?: string) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/purposes', { params: { activeOnly, locationId } })),
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
  mailContents: (locationId?: string) =>
    unwrap(api.get<ApiResponse<MailContentTemplateDto[]>>('/masters/mail-content', {
      params: locationId ? { locationId } : undefined,
    })),
  upsertMailContent: (body: MailContentUpsertRequest) =>
    unwrap(api.put<ApiResponse<MailContentTemplateDto>>('/masters/mail-content', body)),
  meetingHalls: (activeOnly = true, locationId?: string) =>
    unwrap(api.get<ApiResponse<MeetingHallDto[]>>('/masters/meeting-halls', {
      params: { activeOnly, locationId: locationId || undefined },
    })),
  createMeetingHall: (body: MeetingHallUpsertRequest) =>
    unwrap(api.post<ApiResponse<MeetingHallDto>>('/masters/meeting-halls', body)),
  updateMeetingHall: (id: string, body: MeetingHallUpsertRequest) =>
    unwrap(api.put<ApiResponse<MeetingHallDto>>(`/masters/meeting-halls/${id}`, body)),
  idTypes: (activeOnly = true) =>
    unwrap(api.get<ApiResponse<MasterItemDto[]>>('/masters/id-types', { params: { activeOnly } })),
  feedbackQuestions: (activeOnly = true, locationId?: string) =>
    unwrap(
      api.get<ApiResponse<FeedbackQuestionDto[]>>('/masters/feedback-questions', {
        params: { activeOnly, locationId: locationId || undefined },
      }),
    ),
  feedbackResponsesPaged: (page: number, pageSize: number) =>
    unwrap(
      api.get<ApiResponse<PagedResult<VisitFeedbackDto>>>('/masters/feedback-responses/paged', {
        params: { page, pageSize },
      }),
    ),
  createFeedbackQuestion: (body: FeedbackQuestionUpsertRequest) =>
    unwrap(api.post<ApiResponse<FeedbackQuestionDto>>('/masters/feedback-questions', body)),
  updateFeedbackQuestion: (id: string, body: FeedbackQuestionUpsertRequest) =>
    unwrap(api.put<ApiResponse<FeedbackQuestionDto>>(`/masters/feedback-questions/${id}`, body)),
  deactivateFeedbackQuestion: (id: string) =>
    unwrapVoid(api.post<ApiResponse<object>>(`/masters/feedback-questions/${id}/deactivate`)),
  deactivate: (type: string, id: string) =>
    unwrapVoid(api.post<ApiResponse<object>>(`/masters/${type}/${id}/deactivate`)),
  downloadImportTemplate: async (kind: MasterImportKind) => {
    const res = await api.get(`/masters/import/${kind}/template`, { responseType: 'blob' })
    return res.data as Blob
  },
  importExcel: (kind: MasterImportKind, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return unwrap(
      api.post<ApiResponse<MasterImportResultDto>>(`/masters/import/${kind}`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      }),
    )
  },
}

export const settingsApi = {
  get: (locationId?: string) =>
    unwrap(api.get<ApiResponse<SettingsDto>>('/settings', {
      params: locationId ? { locationId } : undefined,
    })),
  getBranding: () => unwrap(api.get<ApiResponse<PublicBrandingDto>>('/settings/branding')),
  getCurrentBranding: () => unwrap(api.get<ApiResponse<PublicBrandingDto>>('/settings/branding/current')),
  update: (body: SettingsDto, locationId?: string) =>
    unwrap(api.put<ApiResponse<SettingsDto>>('/settings', body, {
      params: locationId ? { locationId } : undefined,
    })),
  uploadLogo: (file: File, locationId?: string) => {
    const form = new FormData()
    form.append('file', file)
    return unwrap(
      api.post<ApiResponse<SettingsDto>>('/settings/logo', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        params: locationId ? { locationId } : undefined,
      }),
    )
  },
  getPassSeries: (locationId?: string) =>
    unwrapNullable(api.get<ApiResponse<PassNumberSeriesDto | null>>('/pass-numbers/series', {
      params: locationId ? { locationId } : undefined,
    })),
  upsertPassSeries: (body: UpsertPassNumberSeriesRequest) =>
    unwrap(api.put<ApiResponse<PassNumberSeriesDto>>('/pass-numbers/series', body)),
  sendTestEmail: (to: string, locationId?: string) =>
    unwrapVoid(api.post<ApiResponse<object>>('/settings/test-email', {
      to,
      locationId: locationId || null,
    })),
  listApiKeys: () => unwrap(api.get<ApiResponse<IntegrationApiKeyDto[]>>('/settings/api-keys')),
  createApiKey: (name: string, scopes: IntegrationScope[]) =>
    unwrap(api.post<ApiResponse<CreatedIntegrationApiKeyDto>>('/settings/api-keys', { name, scopes })),
  revokeApiKey: async (id: string) => {
    await api.post(`/settings/api-keys/${id}/revoke`)
  },
}

export const usersApi = {
  list: () => unwrap(api.get<ApiResponse<UserDto[]>>('/users')),
  update: (id: string, body: UpdateUserRequest) =>
    unwrap(api.put<ApiResponse<UserDto>>(`/users/${id}`, body)),
  createForEmployee: (employeeId: string, body: CreateEmployeeLoginRequest) =>
    unwrap(api.post<ApiResponse<EmployeeDto>>(`/users/from-employee/${employeeId}`, body)),
  resetPassword: (id: string, newPassword: string, mustChangePassword = true) =>
    unwrapVoid(api.post<ApiResponse<object>>(`/users/${id}/reset-password`, { newPassword, mustChangePassword })),
  listFaces: (id: string) => unwrap(api.get<ApiResponse<UserFaceLoginDto[]>>(`/users/${id}/faces`)),
  addFace: (id: string, personName: string, photoBase64: string) =>
    unwrap(api.post<ApiResponse<UserFaceLoginDto>>(`/users/${id}/faces`, { personName, photoBase64 })),
  removeFace: (id: string, faceId: string) =>
    unwrapVoid(api.delete<ApiResponse<object>>(`/users/${id}/faces/${faceId}`)),
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
