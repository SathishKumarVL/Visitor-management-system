import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, visitorsApi } from '../lib/api'
import type { MasterItemDto, VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextInput, TextSelect } from '../components/ui/Field'
import { formatDateTime, formatDuration, statusBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { hasAnyRole } from '../lib/utils'

export function CurrentlyInsidePage() {
  const [params] = useSearchParams()
  const checkoutMode = params.get('action') === 'checkout'
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const canCheckout = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Security'])

  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [exitGates, setExitGates] = useState<MasterItemDto[]>([])
  const [exitGateId, setExitGateId] = useState('')
  const [query, setQuery] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await visitorsApi.inside())
    } catch (e) {
      setError(apiErrorMessage(e, 'Unable to load visitors currently inside.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
    if (canCheckout) void mastersApi.exitGates().then(setExitGates)
  }, [load, canCheckout])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return items
    return items.filter(
      (v) =>
        v.visitorName.toLowerCase().includes(q) ||
        (v.companyName ?? '').toLowerCase().includes(q) ||
        (v.hostName ?? '').toLowerCase().includes(q),
    )
  }, [items, query])

  async function checkout(id: string) {
    setBusyId(id)
    setMessage(null)
    try {
      await visitorsApi.checkOut(id, exitGateId || undefined)
      setMessage('Visitor checked out.')
      await load()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div>
      <PageHeader
        eyebrow="On site"
        title="Currently Inside"
        subtitle={checkoutMode ? 'Select a visitor to check out' : 'Live list of visitors on the premises'}
        actions={<Button variant="secondary" onClick={() => void load()}>Refresh</Button>}
      />

      <div className="mb-6 grid gap-3 sm:grid-cols-[auto_1fr] sm:items-end">
        <Panel className="bg-brand-soft px-6 py-5 sm:min-w-[180px]">
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-ink-muted">Total inside</p>
          <p className="mt-1 text-4xl font-semibold text-ink">{items.length}</p>
        </Panel>
        <Panel>
          <FieldLabel htmlFor="insideSearch">Search</FieldLabel>
          <TextInput
            id="insideSearch"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Name, company, or host"
          />
        </Panel>
      </div>

      {canCheckout ? (
        <Panel className="mb-4 max-w-sm">
          <FieldLabel>Exit gate</FieldLabel>
          <TextSelect value={exitGateId} onChange={(e) => setExitGateId(e.target.value)}>
            <option value="">Default</option>
            {exitGates.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </TextSelect>
        </Panel>
      ) : null}

      {error ? (
        <div className="mb-4">
          <Alert tone="error">{error}</Alert>
        </div>
      ) : null}
      {message ? (
        <div className="mb-4">
          <Alert tone="success">{message}</Alert>
        </div>
      ) : null}
      {loading ? <Spinner /> : null}
      {!loading && filtered.length === 0 ? (
        <EmptyState
          title="No visitors currently inside"
          description={query ? 'No matches for your search.' : 'Checked-in visitors will appear here.'}
        />
      ) : null}

      <div className="space-y-3">
        {filtered.map((v) => (
          <Panel key={v.visitId} className={v.isLongStay ? 'border-danger/40 bg-red-50/40' : undefined}>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <Link to={`/visitors/${v.visitId}`} className="text-lg font-semibold text-ink hover:text-primary">
                    {v.visitorName}
                  </Link>
                  <Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge>
                  {v.isLongStay ? <Badge className="bg-red-100 text-danger ring-1 ring-red-200">Long stay</Badge> : null}
                </div>
                <p className="mt-1 text-sm text-ink-muted">
                  {v.companyName} · {v.hostName} · In since {formatDateTime(v.checkInAt)} ·{' '}
                  {formatDuration(v.durationMinutes)}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Link to={`/visitors/${v.visitId}`}>
                  <Button variant="secondary">View</Button>
                </Link>
                {canCheckout ? (
                  <Button
                    variant="primary"
                    disabled={busyId === v.visitId}
                    loading={busyId === v.visitId}
                    onClick={() => void checkout(v.visitId)}
                  >
                    Check Out
                  </Button>
                ) : null}
              </div>
            </div>
          </Panel>
        ))}
      </div>
    </div>
  )
}
