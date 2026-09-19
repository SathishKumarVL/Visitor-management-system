export type Role = 'SuperAdmin' | 'Admin' | 'Reception' | 'Security' | 'Host'

export interface ApiResponse<T> {
  success: boolean
  data: T | null
  message?: string | null
  errors?: string[] | null
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface UserDto {
  id: string
  username: string
  fullName: string
  email: string
  roles: string[]
  departmentId?: string | null
  departmentName?: string | null
  siteId?: string | null
  siteName?: string | null
  /** Sidebar menus this user may see (intersected with role). */
  allowedMenuKeys?: string[]
  mustChangePassword: boolean
  isActive: boolean
}

export interface LoginResponse {
  token: string
  expiresAt: string
  user: UserDto
  refreshToken?: string
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

export interface PublicBrandingDto {
  companyName: string
  logoPath: string
  themePreset?: string
  fontPreset?: string
}

export interface MasterItemDto {
  id: string
  name: string
  isActive: boolean
  sortOrder: number
  code?: string | null
  intercom?: string | null
  requiresPlantNumber: boolean
  requiresOtherText: boolean
  isDefault: boolean
  /** Locations only. Null means the area is shared by every site. */
  siteId?: string | null
  siteName?: string | null
}

export interface SiteDto {
  id: string
  name: string
  code?: string | null
  address?: string | null
  isActive: boolean
  isDefault: boolean
  userCount: number
  locationCount: number
  insideCount: number
}

export interface SiteUpsertRequest {
  name: string
  code?: string | null
  address?: string | null
  isActive: boolean
  isDefault: boolean
}

export interface EmployeeDto {
  id: string
  fullName: string
  email?: string | null
  phone?: string | null
  intercom?: string | null
  designation?: string | null
  departmentId: string
  departmentName: string
  userId?: string | null
  isActive: boolean
}

export interface VisitorListItemDto {
  visitId: string
  visitorId: string
  visitorNumber: string
  visitNumber: string
  visitorName: string
  companyName: string
  phone?: string | null
  email?: string | null
  hostName: string
  departmentName: string
  visitDate: string
  visitTime: string
  status: number | string
  statusLabel: string
  purposes: string[]
  locations: string[]
  photoUrl?: string | null
  checkInAt?: string | null
  checkOutAt?: string | null
  durationMinutes?: number | null
  isLongStay: boolean
  preRegistrationReference?: string | null
  passCode?: string | null
  numberOfPersons?: number
}

export interface ApprovalHistoryDto {
  id: string
  status: number | string
  actionBy?: string | null
  actionAt?: string | null
  rejectionReason?: string | null
  createdAt: string
}

export interface VisitorDetailDto extends VisitorListItemDto {
  intercom?: string | null
  purposeNotes?: string | null
  notes?: string | null
  plantNumber?: string | null
  otherLocationText?: string | null
  idTypeName?: string | null
  idNumberMasked?: string | null
  idNumberFull?: string | null
  idVerificationStatus: number | string
  checkedInBy?: string | null
  checkedOutBy?: string | null
  approvalHistory: ApprovalHistoryDto[]
  feedback?: VisitFeedbackDto | null
}

export interface RegisterVisitorRequest {
  visitorName: string
  visitDate?: string | null
  visitTime?: string | null
  telephone?: string | null
  email: string
  companyName: string
  departmentId: string
  hostEmployeeId?: string | null
  hostName?: string | null
  intercom?: string | null
  purposeIds: string[]
  locationIds: string[]
  plantNumber?: string | null
  otherLocationText?: string | null
  purposeNotes?: string | null
  notes?: string | null
  isWalkIn?: boolean
  photoBase64?: string | null
  /** Preferred: static name Aadhaar | Pan Card | Passport. */
  idTypeName?: string | null
  idTypeId?: string | null
  idNumber?: string | null
  expectedVisitId?: string | null
  recognizedVisitorId?: string | null
  numberOfPersons?: number
  /** Server-allocated when omitted; free-text values are ignored by the API. */
  passNumber?: string | null
}

export interface FaceSearchMatchDto {
  visitorId: string
  visitorNumber: string
  visitorName: string
  companyName: string
  phone?: string | null
  email?: string | null
  photoUrl?: string | null
  lastVisitDate?: string | null
  totalVisits: number
  /** Cosine similarity — higher is a closer match. */
  similarity: number
  /** True when this visitor already has an open (Inside) visit. */
  isCurrentlyInside?: boolean
}

export interface FaceCheckoutMatchDto {
  visitId: string
  visitorId: string
  visitorName: string
  companyName: string
  visitNumber: string
  photoUrl?: string | null
  checkInAt?: string | null
  similarity: number
}

export interface FeedbackQuestionDto {
  id: string
  prompt: string
  isRequired: boolean
  isActive: boolean
  sortOrder: number
}

export interface FeedbackQuestionUpsertRequest {
  prompt: string
  isRequired: boolean
  isActive: boolean
  sortOrder: number
}

export interface SubmitVisitFeedbackRequest {
  answers: { questionId: string; rating: number }[]
  comments?: string | null
}

export interface VisitFeedbackDto {
  id: string
  visitId: string
  visitorId: string
  visitorName: string
  companyName: string
  email?: string | null
  phone?: string | null
  visitNumber: string
  comments?: string | null
  submittedAt: string
  answers: { questionId: string; questionText: string; rating: number }[]
}

/** Result of POST /visitors/validate-photo — exactly one face required. */
export interface FacePhotoValidationDto {
  faceCount: number
  accepted: boolean
  status: string
}

export interface ExpectedVisitorRequest {
  visitorName: string
  companyName: string
  phone?: string | null
  email?: string | null
  expectedDate: string
  expectedTime: string
  hostEmployeeId?: string | null
  hostName?: string | null
  departmentId: string
  purposeIds: string[]
  locationIds: string[]
  plantNumber?: string | null
  otherLocationText?: string | null
  notes?: string | null
}

export interface ChartPointDto {
  label: string
  value: number
}

export interface DashboardDto {
  visitorsToday: number
  currentlyInside: number
  expectedToday: number
  pendingApprovals: number
  checkedOutToday: number
  byDepartment: ChartPointDto[]
  byPurpose: ChartPointDto[]
  byLocation: ChartPointDto[]
  dailyTrend: ChartPointDto[]
  recentVisitors: VisitorListItemDto[]
  pendingApprovalItems: VisitorListItemDto[]
  currentlyInsideItems: VisitorListItemDto[]
}

export interface SettingsDto {
  companyName: string
  logoPath: string
  themePreset: string
  fontPreset: string
  visitorIdPrefix: string
  visitorPassValidityHours: number
  approvalRequired: boolean
  walkInApprovalRequired: boolean
  photoRequired: boolean
  idVerificationRequired: boolean
  maxVisitDurationWarningMinutes: number
  sessionTimeoutMinutes: number
  smtpEnabled: boolean
  smtpHost: string
  smtpPort: number
  smtpEnableSsl: boolean
  smtpUsername: string
  smtpFromAddress: string
  smtpFromName: string
  smtpIgnoreSslErrors: boolean
  /** Write-only: send a new password to update; omit/empty keeps the stored one. */
  smtpPassword?: string | null
  smtpPasswordConfigured: boolean
}

export interface PassDto {
  passId: string
  visitId: string
  passCode: string
  visitorName: string
  companyName: string
  hostName: string
  departmentName: string
  purposes: string[]
  locations: string[]
  visitNumber: string
  checkInAt?: string | null
  photoUrl?: string | null
  status: string
}

export interface CreateUserRequest {
  fullName: string
  username: string
  email: string
  role: string
  departmentId?: string | null
  /** Null grants tenant-wide visibility; a value locks the user to that one site. */
  siteId?: string | null
  allowedMenuKeys?: string[]
  password: string
}

export interface UpdateUserRequest {
  fullName: string
  email: string
  role: string
  departmentId?: string | null
  /** Null grants tenant-wide visibility; a value locks the user to that one site. */
  siteId?: string | null
  allowedMenuKeys?: string[]
  isActive: boolean
}

export interface CreateEmployeeRequest {
  fullName: string
  email?: string | null
  phone?: string | null
  intercom?: string | null
  designation?: string | null
  departmentId: string
  userId?: string | null
}

export interface MasterUpsertRequest {
  name: string
  code?: string | null
  intercom?: string | null
  sortOrder: number
  isActive: boolean
  requiresPlantNumber: boolean
  requiresOtherText: boolean
  isDefault: boolean
  /** Locations only: null keeps the area shared across sites. */
  siteId?: string | null
}

export interface VisitorSearchParams {
  query?: string
  visitorName?: string
  company?: string
  phone?: string
  visitorNumber?: string
  host?: string
  departmentId?: string
  status?: number | string
  dateFrom?: string
  dateTo?: string
  quickFilter?: string
  page?: number
  pageSize?: number
}

export interface VisitorWizardDraft {
  step: number
  visitorName: string
  telephone: string
  email: string
  companyName: string
  visitDate: string
  visitTime: string
  numberOfPersons: number
  departmentId: string
  hostName: string
  purposeIds: string[]
  othersChecked: boolean
  locationIds: string[]
  plantNumber: string
  otherLocationText: string
  otherPurposeText: string
  notes: string
  photoBase64: string
  idTypeName: string
  idNumber: string
  isWalkIn: boolean
  expectedVisitId: string | null
  recognizedVisitorId: string | null
  /** Display-only after server assigns a pass; not sent on register. */
  passNumber: string
}

export interface PassNumberSeriesDto {
  id: string
  siteId?: string | null
  prefix: string
  year?: number | null
  startNumber: number
  endNumber: number
  currentNumber: number
  isActive: boolean
  formatExample: string
}

export interface UpsertPassNumberSeriesRequest {
  siteId?: string | null
  prefix: string
  year?: number | null
  startNumber: number
  endNumber: number
  currentNumber?: number | null
  isActive: boolean
}

export interface PassNumberAllocationDto {
  id: string
  seriesId: string
  userId: string
  userName?: string | null
  fullName?: string | null
  startNumber: number
  endNumber: number
  currentNumber: number
  isActive: boolean
}

export interface UpsertPassNumberAllocationRequest {
  id?: string | null
  seriesId?: string | null
  userId: string
  startNumber: number
  endNumber: number
  currentNumber?: number | null
  isActive: boolean
}
