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

/** Mirrors the backend EmergencyRollCallStatus enum; serialised as its numeric value. */
export const EmergencyRollCall = {
  Unknown: 0,
  Verified: 1,
  Evacuated: 2,
  Missing: 3,
} as const

export type EmergencyRollCallStatus = (typeof EmergencyRollCall)[keyof typeof EmergencyRollCall]

export interface EmergencyRosterItemDto {
  visitId: string
  visitNumber: string
  visitorName: string
  companyName: string
  hostName: string
  departmentName: string
  locations: string[]
  photoUrl?: string | null
  checkInAt?: string | null
  statusLabel: string
  numberOfPersons: number
  rollCallStatus: EmergencyRollCallStatus
  rollCallAt?: string | null
}

export interface EmergencyRosterDto {
  totalInside: number
  visitorsInside: number
  employeeTrackingAvailable: boolean
  verified: number
  evacuated: number
  missing: number
  unaccounted: number
  items: EmergencyRosterItemDto[]
}

export interface EmergencyRollCallResultDto {
  visitId: string
  rollCallStatus: EmergencyRollCallStatus
  rollCallAt: string
}

export interface ApprovalHistoryDto {
  id: string
  status: number | string
  actionBy?: string | null
  actionAt?: string | null
  rejectionReason?: string | null
  createdAt: string
}

export interface AuditLogDto {
  id: string
  action: string
  entity: string
  entityId?: string | null
  userName?: string | null
  description?: string | null
  ipAddress?: string | null
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
  entryGate?: string | null
  exitGate?: string | null
  checkedInBy?: string | null
  checkedOutBy?: string | null
  approvalHistory: ApprovalHistoryDto[]
  auditHistory: AuditLogDto[]
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
  idTypeId?: string | null
  idNumber?: string | null
  expectedVisitId?: string | null
  numberOfPersons?: number
  faceDescriptor?: number[] | null
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
  distance: number
}

export interface ExpectedVisitorRequest {
  visitorName: string
  companyName: string
  phone?: string | null
  email?: string | null
  expectedDate: string
  expectedTime: string
  hostEmployeeId: string
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
  visitorIdPrefix: string
  visitorPassValidityHours: number
  approvalRequired: boolean
  walkInApprovalRequired: boolean
  photoRequired: boolean
  idVerificationRequired: boolean
  maxVisitDurationWarningMinutes: number
  defaultEntryGate: string
  defaultExitGate: string
  sessionTimeoutMinutes: number
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
  password: string
}

export interface UpdateUserRequest {
  fullName: string
  email: string
  role: string
  departmentId?: string | null
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
  idTypeId: string
  idNumber: string
  isWalkIn: boolean
  expectedVisitId: string | null
  faceDescriptor: number[] | null
  recognizedVisitorId: string | null
}
