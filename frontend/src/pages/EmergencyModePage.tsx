import { useCallback, useEffect, useState } from 'react'
import { PageHeader, Panel, Alert, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { SecureImage } from '../components/SecureImage'
import { apiErrorMessage, emergencyApi } from '../lib/api'
import { EmergencyRollCall, type EmergencyRollCallStatus, type EmergencyRosterDto } from '../types/api'
import { formatDateTime } from '../lib/utils'

const ROLL_CALL_ACTIONS: { status: EmergencyRollCallStatus; label: string; danger?: boolean }[] = [
  { status: EmergencyRollCall.Evacuated, label: 'Evacuated' },
  { status: EmergencyRollCall.Verified, label: 'Verified' },
  { status: EmergencyRollCall.Missing, label: 'Missing', danger: true },
]

export function EmergencyModePage() {
  const [roster, setRoster] = useState<EmergencyRosterDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setRoster(await emergencyApi.roster())
      setError(null)
    } catch (e) {
      setError(apiErrorMessage(e, 'Unable to load the emergency roster.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function mark(visitId: string, status: EmergencyRollCallStatus) {
    setBusyId(visitId)
    try {
      await emergencyApi.rollCall(visitId, status)
      // Re-read so the counters and every marshal's view agree on one server-side truth.
      await load()
    } catch (e) {
      setError(apiErrorMessage(e, 'Unable to record the roll call.'))
    } finally {
      setBusyId(null)
    }
  }

  if (loading) return <Spinner label="Loading emergency roster…" />

  const items = roster?.items ?? []

  return (
    <div>
      <PageHeader
        eyebrow="Safety operations"
        title="Emergency Mode"
        subtitle="Live roster of people currently inside. Every mark is recorded and audited."
        actions={
          <Button variant="secondary" onClick={() => void load()}>
            Refresh
          </Button>
        }
      />

      <div aria-live="polite" aria-atomic="true">
        {error ? (
          <div className="mb-4">
            <Alert tone="error">{error}</Alert>
          </div>
        ) : null}
      </div>

      <div className="mb-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5" role="status" aria-live="polite">
        <Panel>
          <p className="text-xs uppercase text-ink-muted">Total inside</p>
          <p className="mt-2 text-3xl font-semibold">{roster?.totalInside ?? 0}</p>
        </Panel>
        <Panel>
          <p className="text-xs uppercase text-ink-muted">Evacuated</p>
          <p className="mt-2 text-3xl font-semibold text-primary">{roster?.evacuated ?? 0}</p>
        </Panel>
        <Panel>
          <p className="text-xs uppercase text-ink-muted">Verified</p>
          <p className="mt-2 text-3xl font-semibold">{roster?.verified ?? 0}</p>
        </Panel>
        <Panel>
          <p className="text-xs uppercase text-ink-muted">Missing</p>
          <p className="mt-2 text-3xl font-semibold text-danger">{roster?.missing ?? 0}</p>
        </Panel>
        <Panel>
          <p className="text-xs uppercase text-ink-muted">Not yet marked</p>
          <p className="mt-2 text-3xl font-semibold text-warning">{roster?.unaccounted ?? 0}</p>
        </Panel>
      </div>

      {roster && !roster.employeeTrackingAvailable ? (
        <div className="mb-6">
          <Alert tone="info">
            This count covers visitors only. Employee presence is not tracked by this system, so employees must be
            accounted for using your existing muster process.
          </Alert>
        </div>
      ) : null}

      <ul className="space-y-3">
        {items.length === 0 ? (
          <li>
            <Panel>
              <p className="text-ink-muted">No visitors currently inside.</p>
            </Panel>
          </li>
        ) : (
          items.map((v) => (
            <li key={v.visitId}>
              <Panel className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex items-center gap-3">
                  <SecureImage
                    src={v.photoUrl}
                    alt={`Photo of ${v.visitorName}`}
                    className="h-14 w-14 rounded-xl object-cover"
                  />
                  <div>
                    <div className="font-semibold text-ink">{v.visitorName}</div>
                    <div className="text-sm text-ink-muted">
                      {v.companyName || '—'} · Host {v.hostName} · {v.locations.join(', ') || 'No location'}
                    </div>
                    <div className="text-xs text-ink-muted">
                      {v.visitNumber} · {v.statusLabel} · In since {formatDateTime(v.checkInAt)}
                      {v.rollCallAt ? ` · Marked ${formatDateTime(v.rollCallAt)}` : ''}
                    </div>
                  </div>
                </div>
                <div
                  className="flex flex-wrap gap-2"
                  role="group"
                  aria-label={`Roll call for ${v.visitorName}`}
                >
                  {ROLL_CALL_ACTIONS.map((action) => {
                    const active = v.rollCallStatus === action.status
                    return (
                      <Button
                        key={action.label}
                        size="sm"
                        variant={active ? (action.danger ? 'danger' : 'primary') : 'secondary'}
                        aria-pressed={active}
                        disabled={busyId === v.visitId}
                        onClick={() => void mark(v.visitId, action.status)}
                      >
                        {action.label}
                      </Button>
                    )
                  })}
                </div>
              </Panel>
            </li>
          ))
        )}
      </ul>
    </div>
  )
}
