import { Navigate } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'
import { homePathForRoles } from '../lib/utils'
import { LoadingMask } from '../components/LoadingMask'

export function HomeRedirect() {
  const { user, initialized } = useAuthStore()
  if (!initialized) return <LoadingMask label="Loading…" />
  if (!user) return <Navigate to="/login" replace />
  return <Navigate to={homePathForRoles(user.roles)} replace />
}
