export type Role = 'SuperAdmin' | 'Admin' | 'Reception' | 'Security' | 'Host'

export interface ApiResponse<T> {
  success: boolean
  data: T | null
  message?: string | null
  errors?: string[] | null
}

export type SortDir = 'asc' | 'desc'

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
  locationId?: string | null
  locationName?: string | null
  /** Sidebar menus this user may see (intersected with role). */
  allowedMenuKeys?: string[]
  mustChangePassword: boolean
  isActive: boolean
  /** Faces registered for face sign-in (admin user list only). */
  faceLoginCount?: number
  /** Employee record this login belongs to; null for shared desk and system accounts (admin user list only). */
  employeeId?: string | null
}

export interface LoginResponse {
  token: string
  expiresAt: string
  user: UserDto
  refreshToken?: string
  /** Face sign-in only: the person whose registered face matched. */
  facePersonName?: string | null
}

export interface UserFaceLoginDto {
  id: string
  personName: string
  createdAt: string
  createdBy?: string | null
  lastUsedAt?: string | null
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
  fontSize?: string
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
  /** Locations only: whether visits here need minutes of meeting. */
  momRequired?: boolean | null
  isDefault: boolean
  locationId?: string | null
  locationName?: string | null
  /** Purposes only: extra text fields asked at walk-in registration. */
  fields?: PurposeFieldDto[] | null
}

export interface PurposeFieldDto {
  /** Empty for a field added in this edit. */
  id?: string | null
  label: string
  isRequired: boolean
  type?: PurposeFieldType | null
  lookupSourceId?: string | null
}

export type PurposeFieldType = 'text' | 'lookup'

export interface LookupDetailDto {
  label: string
  value: string
}

export interface PurposeFieldValueDto {
  purpose: string
  label: string
  value: string
  /** Null for a text field; true when picked from the lookup, false when typed by hand. */
  verified?: boolean | null
  source?: string | null
  recordId?: string | null
  details?: LookupDetailDto[] | null
  matchAgainst?: LookupMatchTarget | null
  matched?: boolean | null
  /** True when others had come for this record before and this visitor never had. */
  newPerson?: boolean | null
}

export type LookupMatchTarget = 'mobile' | 'email'

export interface LookupPick {
  token: string
  recordId?: string
  display: string
  details: LookupDetailDto[]
  hasMatchCheck: boolean
}

export interface LookupSavedFieldDto {
  path: string
  label: string
}

export interface LookupSourceDto {
  id: string
  name: string
  searchUrl: string
  httpMethod: LookupHttpMethod
  bodyTemplate?: string | null
  apiKeyHeader?: string | null
  hasApiKey: boolean
  resultsPath?: string | null
  idPath: string
  displayTemplate: string
  savedFields: LookupSavedFieldDto[]
  matchPath?: string | null
  matchAgainst?: LookupMatchTarget | null
  blockOnMismatch: boolean
  minChars: number
  timeoutSeconds: number
  isActive: boolean
  updatedAt?: string | null
}

export type LookupHttpMethod = 'GET' | 'POST'

export interface LookupSourceUpsertRequest {
  name: string
  searchUrl: string
  httpMethod: LookupHttpMethod
  /** JSON sent with POST; {query} and {field:Label} are filled in. */
  bodyTemplate?: string | null
  apiKeyHeader?: string | null
  /** Blank keeps the saved key. */
  apiKey?: string | null
  clearApiKey?: boolean
  resultsPath?: string | null
  idPath: string
  displayTemplate: string
  savedFields: LookupSavedFieldDto[]
  matchPath?: string | null
  matchAgainst?: LookupMatchTarget | null
  blockOnMismatch: boolean
  minChars: number
  timeoutSeconds: number
  isActive: boolean
}

export interface LookupMatchDto {
  token: string
  recordId: string
  display: string
  details: LookupDetailDto[]
  hasMatchCheck: boolean
  matchValues?: string[] | null
}

export interface LookupTestResultDto {
  ok: boolean
  error?: string | null
  statusCode?: number | null
  recordCount: number
  matches: LookupMatchDto[]
  rawSample?: string | null
}

export interface PreviousPurposeFieldDto {
  fieldId: string
  label: string
  value: string
  lookupSourceId?: string | null
  /** Record and search text of a verified pick; both are needed to check it again. */
  recordId?: string | null
  query?: string | null
}

/** One purpose's extra-field answers from a returning visitor's earlier visit. */
export interface PreviousPurposeAnswersDto {
  purposeId: string
  purposeName: string
  visitDate: string
  fields: PreviousPurposeFieldDto[]
}

export interface LookupRecordVisitorDto {
  visitorId: string
  visitorName: string
  phone?: string | null
  photoUrl?: string | null
  lastCheckInAt: string
  visits: number
}

export type MasterClearType =
  | 'employees'
  | 'departments'
  | 'designations'
  | 'purposes'
  | 'locations'
  | 'feedback-questions'
  | 'meeting-halls'

/** What clearing a whole master removes (preview) or removed. */
export interface MasterClearSummaryDto {
  type: string
  items: { label: string; count: number }[]
}

/** People who came for a picked record (e.g. a student) before. */
export interface LookupRecordVisitorsDto {
  isNewPerson: boolean
  visitors: LookupRecordVisitorDto[]
}

export interface LookupSourceOptionDto {
  id: string
  name: string
  minChars: number
  isActive: boolean
}

export interface LookupCheckResultDto {
  matchAgainst?: LookupMatchTarget | null
  matched?: boolean | null
  blockOnMismatch: boolean
}

export interface EmployeeDto {
  id: string
  title?: EmployeeTitle | null
  fullName: string
  code?: string | null
  email?: string | null
  phone?: string | null
  intercom?: string | null
  designation?: string | null
  departmentId: string
  departmentName: string
  locationId?: string | null
  locationName?: string | null
  userId?: string | null
  /** Login username of the linked Host account. */
  userName?: string | null
  isActive: boolean
  /** ISO date (yyyy-MM-dd). */
  dateOfBirth?: string | null
  gender?: EmployeeGender | null
  /** True when a face is enrolled on the employee record. */
  hasFace?: boolean
  photoUrl?: string | null
}

/** 1 = Male, 2 = Female, 3 = Other. */
export type EmployeeGender = 1 | 2 | 3

export type EmployeeTitle = 'Mr' | 'Ms'

/** Employee menu entry: every field and the face are required (the face only when adding). */
export interface EmployeeProfileRequest {
  title: EmployeeTitle | null
  fullName: string
  code: string
  dateOfBirth: string
  gender: EmployeeGender | null
  phone: string
  email: string
  designation?: string | null
  intercom?: string | null
  departmentId: string
  locationId?: string | null
  isActive: boolean
  facePhotoBase64?: string | null
}

/** Every login is made from an Employee master record and signs in with the face captured there. */
export interface CreateEmployeeLoginRequest {
  username: string
  email?: string | null
  password: string
  /** Defaults to Employee (Host) when omitted. */
  role?: string | null
  allowedMenuKeys?: string[] | null
}

/** A group employee recognised from the face registered on their login, at any location. */
export interface GroupEmployeeMatchDto {
  /** Their login, when they have one. */
  userId?: string | null
  /** Their Employees master record, when one is linked. */
  employeeId?: string | null
  fullName: string
  gender?: EmployeeGender | null
  email?: string | null
  phone?: string | null
  employeeCode?: string | null
  designation?: string | null
  departmentName?: string | null
  locationName?: string | null
  companyName: string
  similarity: number
}

export interface MyDetailsDto {
  username: string
  fullName: string
  email: string
  roles: string[]
  employeeCode?: string | null
  designation?: string | null
  departmentName?: string | null
  locationName?: string | null
  phone?: string | null
  intercom?: string | null
  faceLoginCount: number
}

export type IntegrationScope = 'reports' | 'employees'

export interface IntegrationApiKeyDto {
  id: string
  name: string
  keyPrefix: string
  scopes: IntegrationScope[]
  isActive: boolean
  createdAt: string
  createdBy?: string | null
  lastUsedAt?: string | null
  revokedAt?: string | null
}

export interface CreatedIntegrationApiKeyDto {
  key: IntegrationApiKeyDto
  plainKey: string
}

export interface VisitorListItemDto {
  visitId: string
  visitorId: string
  visitorNumber: string
  visitNumber: string
  visitorName: string
  companyName: string
  gender?: number | null
  salutation?: string
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
  hasMeetingMinutes?: boolean
  /** True once the MOM email has been sent. */
  momEmailSent?: boolean
  /** Minutes of meeting waived for this visit. */
  momNotRequired?: boolean
  momNotRequiredBy?: string | null
  momNotRequiredAt?: string | null
  isManualCheckOut?: boolean
  /** User who checked the visitor out; empty for a face check-out at the desk or the automatic check-out. */
  checkedOutBy?: string | null
  meetingHallName?: string | null
  /** A group employee on official work; visitor emails are not sent. */
  isGroupEmployee?: boolean
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
  hostEmployeeId?: string
  departmentId?: string
  purposeIds?: string[]
  locationIds?: string[]
  intercom?: string | null
  purposeNotes?: string | null
  purposeFields?: PurposeFieldValueDto[]
  notes?: string | null
  /** Minutes of meeting: discussion points. */
  meetingMinutes?: string | null
  momDecisions?: string | null
  momActionItems?: MomActionItemDto[]
  /** yyyy-MM-dd */
  momNextMeetingDate?: string | null
  momRecordedBy?: string | null
  momRecordedAt?: string | null
  momEmailSentAt?: string | null
  plantNumber?: string | null
  otherLocationText?: string | null
  idTypeName?: string | null
  idNumberMasked?: string | null
  idNumberFull?: string | null
  idVerificationStatus: number | string
  checkedInBy?: string | null
  approvalHistory: ApprovalHistoryDto[]
  feedback?: VisitFeedbackDto | null
}

export interface MomActionItemDto {
  task: string
  owner?: string | null
  /** yyyy-MM-dd */
  dueDate?: string | null
}

export interface SaveMeetingMinutesRequest {
  /** Discussion points. */
  meetingMinutes: string
  decisions?: string | null
  actionItems: MomActionItemDto[]
  nextMeetingDate?: string | null
  sendEmail: boolean
}

export interface OpenBookingDto {
  visitId: string
  visitNumber: string
  date: string
  /** HH:mm:ss */
  time?: string | null
  hostEmployeeId: string
  hostName: string
}

/** An existing visitor found by mobile number, with their bookings that are still open. */
export interface VisitorPhoneLookupDto {
  visitorId: string
  visitorNumber: string
  visitorName: string
  companyName: string
  gender?: number | null
  phone?: string | null
  email?: string | null
  lastVisitDate?: string | null
  totalVisits: number
  openBookings: OpenBookingDto[]
}

/** A booking for today that a walk-up registration can be linked to. */
export interface BookingMatchDto {
  visitId: string
  visitorId: string
  visitNumber: string
  visitorName: string
  companyName: string
  gender?: number | null
  phone?: string | null
  email?: string | null
  hostEmployeeId: string
  hostName: string
  departmentId: string
  departmentName: string
  /** HH:mm:ss */
  expectedTime?: string | null
  purposeIds: string[]
  purposes: string[]
  purposeNotes?: string | null
  numberOfPersons: number
}

export interface RegisterVisitorRequest {
  visitorName: string
  /** 1 = Male (Mr.), 2 = Female (Ms.) */
  gender: 1 | 2
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
  purposeFields?: { fieldId: string; value: string; lookupToken?: string | null }[]
  notes?: string | null
  isWalkIn?: boolean
  photoBase64?: string | null
  /** Preferred: static name Aadhaar | Pan Card | Passport. */
  idTypeName?: string | null
  idTypeId?: string | null
  idNumber?: string | null
  /** One per filled-in form; resubmitting it returns the visit already saved instead of a duplicate. */
  clientRequestId?: string | null
  expectedVisitId?: string | null
  recognizedVisitorId?: string | null
  /** Group employee confirmed from the face match: an official visit with no visitor emails. */
  groupEmployeeUserId?: string | null
  /** Employees record of the confirmed group employee (set even when they have no login). */
  groupEmployeeId?: string | null
  /** Set when the desk said a matching booking was not this visitor. */
  skipBookingMatch?: boolean
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
  gender?: number | null
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
  salutation?: string
  companyName: string
  visitNumber: string
  locationId?: string | null
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
  locationId?: string | null
  locationName?: string | null
}

export interface FeedbackQuestionUpsertRequest {
  prompt: string
  isRequired: boolean
  isActive: boolean
  sortOrder: number
  locationId?: string | null
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
  /** 1 = Male (Mr.), 2 = Female (Ms.) */
  gender: 1 | 2
  phone?: string | null
  email?: string | null
  expectedDate: string
  expectedTime: string
  hostEmployeeId?: string | null
  hostName?: string | null
  departmentId: string
  purposeIds: string[]
  purposeNotes?: string | null
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
  /** SuperAdmin manual check-outs today (no face). */
  manualCheckOutsToday?: number
  byDepartment: ChartPointDto[]
  byPurpose: ChartPointDto[]
  byLocation: ChartPointDto[]
  dailyTrend: ChartPointDto[]
  recentVisitors: VisitorListItemDto[]
  pendingApprovalItems: VisitorListItemDto[]
  currentlyInsideItems: VisitorListItemDto[]
  /** Numbers are limited to the signed-in host's own visitors. */
  isHostView?: boolean
  /** Checked-out visits whose MOM email has not been sent (not recorded, or saved but not sent). */
  momPendingCount?: number
  momPendingItems?: VisitorListItemDto[]
  /** SuperAdmin only: checked-out visits with no MOM email sent, including those marked not required. */
  checkedOutWithoutMomCount?: number
  checkedOutWithoutMomItems?: VisitorListItemDto[]
  /** SuperAdmin only: MOM emails not sent, grouped by employee. */
  momNotSentByEmployee?: MomNotSentByEmployeeDto[]
}

export interface MomNotSentByEmployeeDto {
  employeeId: string
  employeeName: string
  departmentName?: string | null
  notRecorded: number
  notSent: number
  notRequired: number
  oldestCheckOutAt?: string | null
}

export interface NotificationItemDto {
  visitId: string
  title: string
  subtitle?: string | null
  /** "HH:mm" for today's appointments. */
  time?: string | null
  /** UTC: check-out for pending minutes, request time for approvals. */
  at?: string | null
  link: string
  /** Arrived since the user last opened the bell. */
  isNew?: boolean
}

export interface NotificationSummaryDto {
  total: number
  /** The badge: items that arrived since the user last opened the bell. */
  unseen: number
  /** False for the desk, whose list is every visitor expected today. */
  appointmentsAreMine: boolean
  appointmentsTodayCount: number
  appointmentsToday: NotificationItemDto[]
  momPendingCount: number
  momPending: NotificationItemDto[]
  approvalsCount: number
  approvals: NotificationItemDto[]
}

export interface WebPushStatusDto {
  publicKey: string
  devices: number
}

export interface SettingsDto {
  companyName: string
  logoPath: string
  themePreset: string
  fontPreset: string
  fontSize: string
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
  purposeFields?: PurposeFieldValueDto[]
  locations: string[]
  visitNumber: string
  checkInAt?: string | null
  photoUrl?: string | null
  status: string
  brandCompanyName?: string
  brandLogoPath?: string
  meetingHallName?: string | null
  meetingHallIntercom?: string | null
  /** Check-in response only: the location has halls but none could take this visit. */
  meetingHallWarning?: string | null
}

export interface MeetingHallDto {
  id: string
  code: string
  name: string
  seatingCapacity: number
  intercomNo?: string | null
  locationId: string
  locationName: string
  isActive: boolean
  occupiedByVisitNumber?: string | null
  occupiedByVisitorName?: string | null
}

export interface MeetingHallUpsertRequest {
  code: string
  name: string
  seatingCapacity: number
  intercomNo?: string | null
  locationId: string | null
  isActive: boolean
}

export type MasterImportKind =
  | 'departments'
  | 'employees'
  | 'purposes'
  | 'locations'
  | 'designations'
  | 'feedback-questions'
  | 'meeting-halls'

export interface MasterImportResultDto {
  totalRows: number
  created: number
  updated: number
  failed: number
  errors: { row: number; message: string }[]
}

export interface UpdateUserRequest {
  fullName: string
  email: string
  role: string
  departmentId?: string | null
  /** Null grants tenant-wide visibility; a value locks the user to that one location. */
  locationId?: string | null
  allowedMenuKeys?: string[]
  isActive: boolean
}

export interface MasterUpsertRequest {
  name: string
  code?: string | null
  intercom?: string | null
  sortOrder: number
  isActive: boolean
  requiresPlantNumber: boolean
  requiresOtherText: boolean
  /** Locations only. Omit to keep the current setting. */
  momRequired?: boolean
  isDefault: boolean
  locationId?: string | null
  /** Purposes only. Omit to keep the current fields; an empty list removes them. */
  fields?: PurposeFieldDto[]
}

export interface MailContentTemplateDto {
  id: string
  kind: number
  kindLabel: string
  subject: string
  body: string
  isActive: boolean
  locationId?: string | null
  locationName?: string | null
}

export interface MailContentUpsertRequest {
  kind: number
  subject: string
  body: string
  isActive: boolean
  locationId?: string | null
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
  sortBy?: string
  sortDir?: SortDir
  page?: number
  pageSize?: number
}

export interface VisitorWizardDraft {
  step: number
  visitorName: string
  /** '' | 1 | 2 — required before submit */
  gender: '' | 1 | 2
  telephone: string
  email: string
  companyName: string
  visitDate: string
  visitTime: string
  numberOfPersons: number
  departmentId: string
  hostName: string
  hostEmployeeId: string
  purposeIds: string[]
  othersChecked: boolean
  locationIds: string[]
  locationId: string
  plantNumber: string
  otherLocationText: string
  otherPurposeText: string
  /** Extra purpose field answers by field id. */
  purposeFieldValues: Record<string, string>
  /** Record picked from a lookup field, by field id; dropped as soon as the text is edited by hand. */
  purposeLookupPicks: Record<string, LookupPick>

  notes: string
  photoBase64: string
  idTypeName: string
  idNumber: string
  isWalkIn: boolean
  expectedVisitId: string | null
  recognizedVisitorId: string | null
  groupEmployeeUserId: string | null
  groupEmployeeId: string | null
  /** Display-only after server assigns a pass; not sent on register. */
  passNumber: string
}

export interface PassNumberSeriesDto {
  id: string
  locationId?: string | null
  prefix: string
  year?: number | null
  startNumber: number
  endNumber: number
  currentNumber: number
  isActive: boolean
  formatExample: string
}

export interface UpsertPassNumberSeriesRequest {
  locationId?: string | null
  prefix: string
  year?: number | null
  startNumber: number
  endNumber: number
  currentNumber?: number | null
  isActive: boolean
}