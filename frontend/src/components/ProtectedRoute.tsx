import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'
import type { Role } from '../types/api'
import { hasAnyRole } from '../lib/utils'
import { grantedByMenu } from '../lib/roles'
import { LoadingMask } from './LoadingMask'

export function ProtectedRoute({ roles }: { roles?: Role[] }) {
  const { user, token, initialized } = useAuthStore()
  const location = useLocation()

  if (!initialized) return <LoadingMask label="Checking session…" />
  if (!token || !user) {
    return <Navigate to={`/login?redirect=${encodeURIComponent(location.pathname + location.search)}`} replace />
  }
  if (user.mustChangePassword && location.pathname !== '/change-password') {
    return <Navigate to="/change-password" replace />
  }
  if (roles && !hasAnyRole(user.roles, roles) && !grantedByMenu(user.roles, location.pathname, user.allowedMenuKeys)) {
    return <Navigate to="/unauthorized" replace />
  }
  return <Outlet />
}
