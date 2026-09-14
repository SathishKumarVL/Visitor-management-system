import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'
import type { Role } from '../types/api'
import { hasAnyRole } from '../lib/utils'
import { Spinner } from './ui/Panel'

export function ProtectedRoute({ roles }: { roles?: Role[] }) {
  const { user, token, initialized } = useAuthStore()
  const location = useLocation()

  if (!initialized) return <Spinner label="Checking session…" />
  if (!token || !user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }
  if (user.mustChangePassword && location.pathname !== '/change-password') {
    return <Navigate to="/change-password" replace />
  }
  if (roles && !hasAnyRole(user.roles, roles)) {
    return <Navigate to="/unauthorized" replace />
  }
  return <Outlet />
}
