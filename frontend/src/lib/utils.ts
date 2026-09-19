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

export function formatDate(value?: string | null, pattern = 'dd MMM yyyy'): string {
  if (!value) return '—'
  const d = value.length <= 10 ? parseISO(value) : new Date(value)
  return isValid(d) ? format(d, pattern) : value
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  const d = new Date(value)
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
      preferred = '/host'
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
    '/host': 'host',
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
    'dashboard', 'reception', 'host', 'visitors', 'inside', 'expected',
    'reports', 'departments', 'purposes', 'locations', 'feedback', 'sites', 'users', 'settings',
  ]
  const pathByKey: Record<string, string> = {
    dashboard: '/dashboard',
    reception: '/reception',
    host: '/host',
    visitors: '/visitors',
    inside: '/visitors/inside',
    expected: '/visitors/expected',
    reports: '/reports',
    departments: '/masters/departments',
    purposes: '/masters/purposes',
    locations: '/masters/locations',
    feedback: '/masters/feedback',
    sites: '/masters/sites',
    users: '/users',
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

export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}
