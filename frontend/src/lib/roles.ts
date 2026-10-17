import type { Role } from '../types/api'
import { hasAnyRole } from './utils'

export type MenuKey =
  | 'dashboard'
  | 'visitors'
  | 'inside'
  | 'expected'
  | 'reports'
  | 'departments'
  | 'employees'
  | 'purposes'
  | 'locations'
  | 'feedback'
  | 'mailcontent'
  | 'users'
  | 'meetinghalls'
  | 'settings'
  | 'reception'
  | 'mydetails'
  | 'employeemaster'

export type MenuGroup = 'masters'

/** The Host role is shown as "Employee": an employee's own login, linked to their Employees master record. */
export function roleLabel(role: string): string {
  return role === 'Host' ? 'Employee' : role
}

export interface MenuItem {
  key: MenuKey
  label: string
  path: string
  roles: Role[]
  /** Shown as a sub-menu under this group in the sidebar. */
  group?: MenuGroup
  /**
   * Reached from this page instead: hidden from the sidebar when the user can open it, and that entry is
   * highlighted while on this page. Access rules are unchanged.
   */
  reachedFrom?: MenuKey
  /** Always shown for its roles; not part of the per-user menu allowlist. */
  alwaysVisible?: boolean
}

export const MENU_ITEMS: MenuItem[] = [
  { key: 'dashboard', label: 'Dashboard', path: '/dashboard', roles: ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host'] },
  { key: 'reception', label: 'Reception', path: '/reception', roles: ['SuperAdmin', 'Admin', 'Reception'] },
  { key: 'visitors', label: 'Visitors', path: '/visitors', roles: ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host'] },
  { key: 'inside', label: 'Currently Inside', path: '/visitors/inside', roles: ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host'] },
  { key: 'expected', label: 'Expected Visitors', path: '/visitors/expected', roles: ['SuperAdmin', 'Admin', 'Reception', 'Host', 'Security'], reachedFrom: 'reception' },
  { key: 'reports', label: 'Reports', path: '/reports', roles: ['SuperAdmin', 'Admin', 'Reception'] },
  { key: 'employeemaster', label: 'Employees', path: '/employee-master', roles: ['SuperAdmin', 'Admin'], group: 'masters' },
  { key: 'departments', label: 'Departments', path: '/masters/departments', roles: ['SuperAdmin', 'Admin'], group: 'masters' },
  { key: 'purposes', label: 'Purposes', path: '/masters/purposes', roles: ['SuperAdmin', 'Admin'], group: 'masters' },
  { key: 'locations', label: 'Locations', path: '/masters/locations', roles: ['SuperAdmin', 'Admin'], group: 'masters' },
  { key: 'feedback', label: 'Feedback', path: '/masters/feedback', roles: ['SuperAdmin', 'Admin'], group: 'masters' },
  { key: 'mailcontent', label: 'Mail Content', path: '/masters/mail-content', roles: ['SuperAdmin', 'Admin'], group: 'masters' },
  { key: 'meetinghalls', label: 'Meeting Halls', path: '/masters/meeting-halls', roles: ['SuperAdmin', 'Admin'], group: 'masters' },
  { key: 'settings', label: 'Settings', path: '/settings', roles: ['SuperAdmin', 'Admin'] },
  { key: 'mydetails', label: 'My details', path: '/my-details', roles: ['Host'], alwaysVisible: true },
]

/** Drop retired hub keys from allowlists. */
function normalizeAllowKeys(keys: string[]): Set<string> {
  const allow = new Set(keys.map((k) => k.toLowerCase()))
  allow.delete('security')
  allow.delete('emergency')
  allow.delete('host')
  allow.delete('sites')
  return allow
}

export function visibleMenu(roles: string[], allowedMenuKeys?: string[] | null): MenuItem[] {
  const byRole = MENU_ITEMS.filter((item) => hasAnyRole(roles, item.roles))
  // SuperAdmin always sees the full SuperAdmin catalog (new menus must not wait on an old allowlist).
  if (hasAnyRole(roles, ['SuperAdmin'])) return byRole
  if (!allowedMenuKeys || allowedMenuKeys.length === 0) return byRole
  const allow = normalizeAllowKeys(allowedMenuKeys)
  return byRole.filter((item) => item.alwaysVisible || allow.has(item.key))
}

/** Menus an admin can switch on or off for a user of this role. */
export function menusForRole(role: string): MenuItem[] {
  return MENU_ITEMS.filter((item) => !item.alwaysVisible && hasAnyRole([role], item.roles))
}

export function canAccessPath(roles: string[], path: string, allowedMenuKeys?: string[] | null): boolean {
  const item = MENU_ITEMS.find((m) => path === m.path || path.startsWith(`${m.path}/`))
  if (!item) return true
  return visibleMenu(roles, allowedMenuKeys).some((m) => m.key === item.key)
}
