import { useEffect, useState } from 'react'
import { PageHeader, Panel, Alert, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { SecureImage } from '../components/SecureImage'
import { apiErrorMessage, visitorsApi } from '../lib/api'
import type { VisitorListItemDto } from '../types/api'
import { formatDateTime } from '../lib/utils'

type EvacState = 'unknown' | 'evacuated' | 'verified' | 'missing'

export function EmergencyModePage() {
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [status, setStatus] = useState<Record<string, EvacState>>({})
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    void (async () => {
      try {
        const inside = await visitorsApi.inside()
        setItems(inside)
        const initial: Record<string, EvacState> = {}
        for (const v of inside) initial[v.visitId] = 'unknown'
        setStatus(initial)
      } catch (e) {
        setError(apiErrorMessage(e, 'Unable to load currently inside list.'))
      } finally {
        setLoading(false)
      }
    })()
  }, [])

  function mark(id: string, next: EvacState) {
    setStatus((prev) => ({ ...prev, [id]: next }))
  }

  const counts = {
    total: items.length,
    evacuated: Object.values(status).filter((s) => s === 'evacuated').length,
    verified: Object.values(status).filter((s) => s === 'verified').length,
    missing: Object.values(status).filter((s) => s === 'missing').length,
  }

  if (loading) return <Spinner label="Loading emergency roster…" />

  return (
    <div>
      <PageHeader
        eyebrow="Safety operations"
        title="Emergency Mode"
        subtitle="Live roster of people currently inside. Mark evacuated, verified, or missing."
      />

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}

      <div className="mb-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Panel><p className="text-xs uppercase text-ink-muted">Total inside</p><p className="mt-2 text-3xl font-semibold">{counts.total}</p></Panel>
        <Panel><p className="text-xs uppercase text-ink-muted">Evacuated</p><p className="mt-2 text-3xl font-semibold text-primary">{counts.evacuated}</p></Panel>
        <Panel><p className="text-xs uppercase text-ink-muted">Verified</p><p className="mt-2 text-3xl font-semibold">{counts.verified}</p></Panel>
        <Panel><p className="text-xs uppercase text-ink-muted">Missing</p><p className="mt-2 text-3xl font-semibold text-danger">{counts.missing}</p></Panel>
      </div>

      <div className="space-y-3">
        {items.length === 0 ? (
          <Panel><p className="text-ink-muted">No visitors currently inside.</p></Panel>
        ) : (
          items.map((v) => (
            <Panel key={v.visitId} className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div className="flex items-center gap-3">
                <SecureImage src={v.photoUrl} alt={v.visitorName} className="h-14 w-14 rounded-xl object-cover" />
                <div>
                  <div className="font-semibold text-ink">{v.visitorName}</div>
                  <div className="text-sm text-ink-muted">
                    {v.visitNumber} · {v.locations.join(', ') || '—'} · Host {v.hostName}
                  </div>
                  <div className="text-xs text-ink-muted">In since {formatDateTime(v.checkInAt)}</div>
                </div>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button size="sm" variant={status[v.visitId] === 'evacuated' ? 'primary' : 'secondary'} onClick={() => mark(v.visitId, 'evacuated')}>Evacuated</Button>
                <Button size="sm" variant={status[v.visitId] === 'verified' ? 'primary' : 'secondary'} onClick={() => mark(v.visitId, 'verified')}>Verified</Button>
                <Button size="sm" variant={status[v.visitId] === 'missing' ? 'danger' : 'secondary'} onClick={() => mark(v.visitId, 'missing')}>Missing</Button>
              </div>
            </Panel>
          ))
        )}
      </div>
    </div>
  )
}
