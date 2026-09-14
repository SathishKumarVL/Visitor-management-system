import { useOnlineStatus } from '../../hooks/useOnlineStatus'

export function OfflineBanner() {
  const online = useOnlineStatus()
  if (online) return null
  return (
    <div className="no-print bg-warning px-4 py-2 text-center text-sm font-semibold text-white" role="status">
      Network connection unavailable. Reconnect to continue submitting and syncing.
    </div>
  )
}
