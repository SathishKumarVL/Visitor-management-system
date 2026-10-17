import clsx, { type ClassValue } from 'clsx'
import { format, parseISO, isValid } from 'date-fns'
import type { Role } from '../types/api'

export function cn(...inputs: ClassValue[]) {
  return clsx(inputs)
}

/** Resolve static asset paths (logo, photos) against API origin in prod, or same-origin proxy in dev. */
export function assetUrl(path?: string | null): string {
  if (!path) return '/branding/tiaano-logo.png'
  if (path.startsWith('http://') || path.startsWith('https://') || path.startsWith('data:')) return path
  const normalized = path.startsWith('/') ? path : `/${path}`
  const apiOrigin = import.meta.env.VITE_API_ORIGIN as string | undefined
  if (apiOrigin) return `${apiOrigin.replace(/\/$/, '')}${normalized}`
  return normalized
}

/** "Ms. Priya Kumar" from the Employee master title and name. */
export function titledName(person: { title?: string | null; fullName: string }): string {
  return person.title ? `${person.title}. ${person.fullName}` : person.fullName
}

/** Random v4 UUID; crypto.randomUUID is missing when the app is opened over plain http on the LAN. */
export function newUuid(): string {
  const b = crypto.getRandomValues(new Uint8Array(16))
  b[6] = (b[6] & 0x0f) | 0x40
  b[8] = (b[8] & 0x3f) | 0x80
  const h = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

export function formatDate(value?: string | null, pattern = 'dd MMM yyyy'): string {
  if (!value) return '—'
  const d = value.length <= 10 ? parseISO(value) : new Date(value)
  return isValid(d) ? format(d, pattern) : value
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  // API stores UTC; SQL/JSON often omit "Z", so treat timezone-less values as UTC.
  const normalized =
    /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value.trim()) || value.length <= 10
      ? value
      : `${value.trim()}Z`
  const d = new Date(normalized)
  return isValid(d) ? format(d, 'dd MMM yyyy HH:mm') : value
}

export function formatDuration(minutes?: number | null): string {
  if (minutes == null) return '—'
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h <= 0) return `${m}m`
  return `${h}h ${m}m`
}

export function primaryRole(roles: string[]): Role | null {
  const order: Role[] = ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host']
  for (const r of order) {
    if (roles.includes(r)) return r
  }
  return (roles[0] as Role) ?? null
}

export function homePathForRoles(roles: string[], allowedMenuKeys?: string[] | null): string {
  const role = primaryRole(roles)
  let preferred = '/dashboard'
  switch (role) {
    case 'Reception':
      preferred = '/reception'
      break
    case 'Security':
      preferred = '/visitors/inside'
      break
    case 'Host':
      preferred = '/dashboard'
      break
    default:
      preferred = '/dashboard'
  }

  // Prefer role home when still allowed; otherwise first visible menu.
  // Import deferred via dynamic check to avoid circular deps — caller may also use visibleMenu.
  if (!allowedMenuKeys || allowedMenuKeys.length === 0) return preferred

  const keyForPath: Record<string, string> = {
    '/dashboard': 'dashboard',
    '/reception': 'reception',
    '/visitors/inside': 'inside',
  }
  const preferredKey = keyForPath[preferred]
  const allow = new Set(allowedMenuKeys.map((k) => k.toLowerCase()))
  // Retired hub keys — treat as inside so Security users are not locked out.
  if (allow.has('security') || allow.has('emergency')) {
    allow.add('inside')
    allow.delete('security')
    allow.delete('emergency')
  }
  if (preferredKey && allow.has(preferredKey)) return preferred

  const order = [
    'dashboard', 'reception', 'visitors', 'inside', 'expected',
    'reports', 'employeemaster', 'departments', 'purposes', 'locations', 'feedback', 'meetinghalls', 'settings',
  ]
  const pathByKey: Record<string, string> = {
    dashboard: '/dashboard',
    reception: '/reception',
    visitors: '/visitors',
    inside: '/visitors/inside',
    expected: '/visitors/expected',
    reports: '/reports',
    departments: '/masters/departments',
    purposes: '/masters/purposes',
    locations: '/masters/locations',
    feedback: '/masters/feedback',
    employeemaster: '/employee-master',
    meetinghalls: '/masters/meeting-halls',
    settings: '/settings',
  }
  for (const key of order) {
    if (allow.has(key) && pathByKey[key]) return pathByKey[key]
  }
  return preferred
}

export function hasAnyRole(userRoles: string[], allowed: Role[]): boolean {
  return allowed.some((r) => userRoles.includes(r))
}

export function statusBadgeClass(statusLabel: string): string {
  const s = statusLabel.toLowerCase()
  if (s.includes('inside')) return 'bg-emerald-100 text-success ring-1 ring-emerald-200'
  if (s.includes('pending')) return 'bg-amber-50 text-warning ring-1 ring-amber-200'
  if (s.includes('approved') || s.includes('expected')) return 'bg-aqua-light text-primary-deep ring-1 ring-primary/20'
  if (s.includes('reject') || s.includes('cancel')) return 'bg-red-100 text-danger ring-1 ring-red-200'
  if (s.includes('check')) return 'bg-gray-100 text-gray-700 ring-1 ring-gray-200'
  return 'bg-gray-100 text-gray-700 ring-1 ring-gray-200'
}

export function todayIsoDate(): string {
  return format(new Date(), 'yyyy-MM-dd')
}

export function nowIsoTime(): string {
  return format(new Date(), 'HH:mm')
}

/** Next quarter-hour at least a minute away, e.g. 10:07 -> 10:15; rolls over to tomorrow near midnight. */
export function nextBookingSlot(): { date: string; time: string } {
  const d = new Date()
  d.setSeconds(0, 0)
  d.setMinutes(Math.ceil((d.getMinutes() + 1) / 15) * 15)
  return { date: format(d, 'yyyy-MM-dd'), time: format(d, 'HH:mm') }
}

/** True when a local yyyy-MM-dd + HH:mm is already in the past (missing parts count as not past). */
export function isPastDateTime(date: string, time: string): boolean {
  if (!date || !time) return false
  const when = new Date(`${date}T${time}`)
  return !Number.isNaN(when.getTime()) && when.getTime() < Date.now() - 60_000
}

const EMAIL_MAX_LENGTH = 150

/** Visitor email: required and must look like a valid address. */
export function validateVisitorEmail(value: string): string | null {
  const email = value.trim()
  if (!email) return 'Email is required.'
  if (email.length > EMAIL_MAX_LENGTH) {
    return `Email must be at most ${EMAIL_MAX_LENGTH} characters.`
  }
  if (/\s/.test(email)) return 'Email cannot contain spaces.'
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    return 'Enter a valid email address (e.g. name@company.com).'
  }
  return null
}

/** Keep only digits, max 10 (Indian mobile without country code). */
export function sanitizePhoneDigits(value: string): string {
  return value.replace(/\D/g, '').slice(0, 10)
}

/**
 * Optional phone: empty is OK. When provided, must be exactly 10 digits.
 * Returns normalized digits, or throws-style message string on error.
 */
export function validateOptionalTenDigitPhone(value: string): { ok: true; phone: string | null } | { ok: false; error: string } {
  const digits = sanitizePhoneDigits(value)
  if (!digits) return { ok: true, phone: null }
  if (digits.length !== 10) return { ok: false, error: 'Phone number must be exactly 10 digits.' }
  return { ok: true, phone: digits }
}

export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}
