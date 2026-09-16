import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiErrorMessage, approvalsApi } from '../lib/api'
import type { VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldError, FieldLabel, TextInput } from '../components/ui/Field'
import { SecureImage } from '../components/SecureImage'
import { formatDateTime, statusBadgeClass } from '../lib/utils'

/**
 * Host and admin decision queue. Only reachable for tenants that turned approval on;
 * for everyone else the queue is simply empty.
 */
export function ApprovalsPage() {
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [query, setQuery] = useState('')

  const [rejectingId, setRejectingId] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const reasonRef = useRef<HTMLInputElement>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await approvalsApi.list())
    } catch (e) {
      setError(apiErrorMessage(e, 'Unable to load pending approvals.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  // Move focus into the reason field as soon as a rejection is started.
  useEffect(() => {
    if (rejectingId) reasonRef.current?.focus()
  }, [rejectingId])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return items
    return items.filter(
      (v) =>
        v.visitorName.toLowerCase().includes(q) ||
        (v.companyName ?? '').toLowerCase().includes(q) ||
        (v.hostName ?? '').toLowerCase().includes(q) ||
        v.visitNumber.toLowerCase().includes(q),
    )
  }, [items, query])

  function startReject(id: string) {
    setRejectingId(id)
    setReason('')
    setReasonError(null)
    setMessage(null)
  }

  function cancelReject() {
    setRejectingId(null)
    setReason('')
    setReasonError(null)
  }

  async function approve(visit: VisitorListItemDto) {
    setBusyId(visit.visitId)
    setError(null)
    setMessage(null)
    try {
      await approvalsApi.approve(visit.visitId)
      setMessage(`${visit.visitorName} approved. Reception can now check them in.`)
      await load()
    } catch (e) {
      setError(apiErrorMessage(e, 'Unable to approve this visit.'))
    } finally {
      setBusyId(null)
    }
  }

  async function reject(visit: VisitorListItemDto) {
    const trimmed = reason.trim()
    if (!trimmed) {
      setReasonError('A reason is required so the visitor and reception know why.')
      reasonRef.current?.focus()
      return
    }

    setBusyId(visit.visitId)
    setError(null)
    setMessage(null)
    try {
      await approvalsApi.reject(visit.visitId, trimmed)
      setMessage(`${visit.visitorName} rejected.`)
      cancelReject()
      await load()
    } catch (e) {
      setError(apiErrorMessage(e, 'Unable to reject this visit.'))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div>
      <PageHeader
        eyebrow="Host"
        title="Pending Approvals"
        subtitle="Visitors waiting on a decision before reception can check them in"
        actions={
          <Button variant="secondary" onClick={() => void load()}>
            Refresh
          </Button>
        }
      />

      <div className="mb-6 grid gap-3 sm:grid-cols-[auto_1fr] sm:items-end">
        <Panel className="bg-brand-soft px-6 py-5 sm:min-w-[180px]">
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-ink-muted">Awaiting decision</p>
          <p className="mt-1 text-4xl font-semibold text-ink">{items.length}</p>
        </Panel>
        <Panel>
          <FieldLabel htmlFor="approvalSearch">Search</FieldLabel>
          <TextInput
            id="approvalSearch"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Name, company, host, or visit number"
          />
        </Panel>
      </div>

      <div aria-live="polite" aria-atomic="true">
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
      </div>

      {loading ? <Spinner label="Loading approvals…" /> : null}

      {!loading && filtered.length === 0 ? (
        <EmptyState
          title={query ? 'No matching approvals' : 'Nothing waiting for approval'}
          description={
            query
              ? 'No pending visit matches your search.'
              : 'Visits appear here only while they are waiting for a host decision.'
          }
        />
      ) : null}

      <ul className="space-y-3">
        {filtered.map((v) => {
          const busy = busyId === v.visitId
          const isRejecting = rejectingId === v.visitId
          return (
            <li key={v.visitId}>
              <Panel>
                <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                  <div className="flex gap-4">
                    <SecureImage
                      src={v.photoUrl}
                      alt={`Photo of ${v.visitorName}`}
                      className="h-16 w-16 shrink-0 rounded-xl object-cover"
                    />
                    <div>
                      <div className="flex flex-wrap items-center gap-2">
                        <Link
                          to={`/visitors/${v.visitId}`}
                          className="text-lg font-semibold text-ink hover:text-primary"
                        >
                          {v.visitorName}
                        </Link>
                        <Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge>
                      </div>
                      <p className="mt-1 text-sm text-ink-muted">
                        {v.companyName} · Host {v.hostName} · {v.departmentName}
                      </p>
                      <p className="mt-0.5 text-sm text-ink-muted">
                        {v.visitNumber} · {formatDateTime(`${v.visitDate}T${v.visitTime}`)}
                        {v.purposes.length ? ` · ${v.purposes.join(', ')}` : ''}
                      </p>
                    </div>
                  </div>

                  <div className="flex flex-wrap gap-2 lg:justify-end">
                    <Button
                      variant="primary"
                      disabled={busy || isRejecting}
                      loading={busy && !isRejecting}
                      onClick={() => void approve(v)}
                    >
                      Approve
                    </Button>
                    {!isRejecting ? (
                      <Button variant="secondary" disabled={busy} onClick={() => startReject(v.visitId)}>
                        Reject
                      </Button>
                    ) : null}
                  </div>
                </div>

                {isRejecting ? (
                  <div className="mt-4 rounded-xl border border-danger/25 bg-red-50/50 p-4">
                    <FieldLabel htmlFor={`reason-${v.visitId}`}>Reason for rejection</FieldLabel>
                    <TextInput
                      ref={reasonRef}
                      id={`reason-${v.visitId}`}
                      value={reason}
                      onChange={(e) => {
                        setReason(e.target.value)
                        if (reasonError) setReasonError(null)
                      }}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                          e.preventDefault()
                          void reject(v)
                        }
                        if (e.key === 'Escape') cancelReject()
                      }}
                      placeholder="e.g. Host unavailable today"
                      aria-invalid={reasonError ? true : undefined}
                      aria-describedby={reasonError ? `reason-error-${v.visitId}` : undefined}
                    />
                    <div id={`reason-error-${v.visitId}`}>
                      <FieldError message={reasonError} />
                    </div>
                    <div className="mt-3 flex flex-wrap gap-2">
                      <Button variant="danger" disabled={busy} loading={busy} onClick={() => void reject(v)}>
                        Confirm rejection
                      </Button>
                      <Button variant="secondary" disabled={busy} onClick={cancelReject}>
                        Cancel
                      </Button>
                    </div>
                  </div>
                ) : null}
              </Panel>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
