import type { Role } from '../types/api'
import { hasAnyRole } from './utils'

export type MenuKey =
  | 'dashboard'
  | 'visitors'
  | 'inside'
  | 'expected'
  | 'reports'
  | 'departments'
  | 'purposes'
  | 'locations'
  | 'sites'
  | 'users'
  | 'settings'
  | 'reception'
  | 'security'
  | 'host'

export interface MenuItem {
  key: MenuKey
  label: string
  path: string
  roles: Role[]
}

export const MENU_ITEMS: MenuItem[] = [
  { key: 'dashboard', label: 'Dashboard', path: '/dashboard', roles: ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host'] },
  { key: 'reception', label: 'Reception', path: '/reception', roles: ['SuperAdmin', 'Admin', 'Reception'] },
  { key: 'security', label: 'Security', path: '/security', roles: ['SuperAdmin', 'Admin', 'Security'] },
  { key: 'host', label: 'Host Desk', path: '/host', roles: ['SuperAdmin', 'Admin', 'Host'] },
  { key: 'visitors', label: 'Visitors', path: '/visitors', roles: ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host'] },
  { key: 'inside', label: 'Currently Inside', path: '/visitors/inside', roles: ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host'] },
  { key: 'expected', label: 'Expected Visitors', path: '/visitors/expected', roles: ['SuperAdmin', 'Admin', 'Reception', 'Host', 'Security'] },
  { key: 'reports', label: 'Reports', path: '/reports', roles: ['SuperAdmin', 'Admin', 'Reception'] },
  { key: 'departments', label: 'Departments', path: '/masters/departments', roles: ['SuperAdmin', 'Admin'] },
  { key: 'purposes', label: 'Purposes', path: '/masters/purposes', roles: ['SuperAdmin', 'Admin'] },
  { key: 'locations', label: 'Locations', path: '/masters/locations', roles: ['SuperAdmin', 'Admin'] },
  { key: 'sites', label: 'Sites', path: '/masters/sites', roles: ['SuperAdmin', 'Admin'] },
  { key: 'users', label: 'Users', path: '/users', roles: ['SuperAdmin', 'Admin'] },
  { key: 'settings', label: 'Settings', path: '/settings', roles: ['SuperAdmin', 'Admin'] },
]

export function visibleMenu(roles: string[], allowedMenuKeys?: string[] | null): MenuItem[] {
  const byRole = MENU_ITEMS.filter((item) => hasAnyRole(roles, item.roles))
  if (!allowedMenuKeys || allowedMenuKeys.length === 0) return byRole
  const allow = new Set(allowedMenuKeys.map((k) => k.toLowerCase()))
  return byRole.filter((item) => allow.has(item.key))
}

export function menusForRole(role: string): MenuItem[] {
  return MENU_ITEMS.filter((item) => hasAnyRole([role], item.roles))
}

export function canAccessPath(roles: string[], path: string, allowedMenuKeys?: string[] | null): boolean {
  const item = MENU_ITEMS.find((m) => path === m.path || path.startsWith(`${m.path}/`))
  if (!item) return true
  return visibleMenu(roles, allowedMenuKeys).some((m) => m.key === item.key)
}
