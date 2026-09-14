import { Navigate } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'
import { homePathForRoles } from '../lib/utils'
import { Spinner } from '../components/ui/Panel'

export function HomeRedirect() {
  const { user, initialized } = useAuthStore()
  if (!initialized) return <Spinner />
  if (!user) return <Navigate to="/login" replace />
  return <Navigate to={homePathForRoles(user.roles)} replace />
}
