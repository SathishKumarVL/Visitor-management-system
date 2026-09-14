import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, passApi, visitorsApi } from '../lib/api'
import type { MasterItemDto, VisitorDetailDto } from '../types/api'
import { Alert, Badge, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextSelect } from '../components/ui/Field'
import { formatDate, formatDateTime, formatDuration, statusBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { hasAnyRole } from '../lib/utils'
import { SecureImage } from '../components/SecureImage'

export function VisitorDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const canGate = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Security'])

  const [visitor, setVisitor] = useState<VisitorDetailDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [entryGates, setEntryGates] = useState<MasterItemDto[]>([])
  const [exitGates, setExitGates] = useState<MasterItemDto[]>([])
  const [entryGateId, setEntryGateId] = useState('')
  const [exitGateId, setExitGateId] = useState('')

  async function load() {
    if (!id) return
    setLoading(true)
    setError(null)
    try {
      setVisitor(await visitorsApi.get(id))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
    if (canGate) {
      void Promise.all([mastersApi.entryGates(), mastersApi.exitGates()]).then(([a, b]) => {
        setEntryGates(a)
        setExitGates(b)
      })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  async function checkIn() {
    if (!id) return
    setBusy(true)
    setMessage(null)
    try {
      const pass = await visitorsApi.checkIn(id, entryGateId || undefined)
      setMessage('Checked in successfully.')
      navigate(`/passes/${id}/print`, { state: { pass } })
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function checkOut() {
    if (!id) return
    setBusy(true)
    setMessage(null)
    try {
      await visitorsApi.checkOut(id, exitGateId || undefined)
      setMessage('Checked out successfully.')
      await load()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function printPass() {
    if (!id) return
    setBusy(true)
    try {
      await passApi.generate(id)
      navigate(`/passes/${id}/print`)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  if (loading) return <Spinner />
  if (error && !visitor) return <Alert tone="error">{error}</Alert>
  if (!visitor) return null

  const status = String(visitor.statusLabel).toLowerCase()

  return (
    <div>
      <PageHeader
        title={visitor.visitorName}
        subtitle={`${visitor.visitNumber} · ${visitor.companyName}`}
        actions={
          <div className="flex flex-wrap gap-2">
            <Link to="/visitors"><Button variant="secondary">Back to list</Button></Link>
            {canGate && (status.includes('approved') || status.includes('expected')) ? (
              <Button disabled={busy} onClick={() => void checkIn()}>Check in</Button>
            ) : null}
            {canGate && status.includes('inside') ? (
              <Button variant="danger" disabled={busy} onClick={() => void checkOut()}>Check out</Button>
            ) : null}
            {canGate ? (
              <Button variant="amber" disabled={busy} onClick={() => void printPass()}>Print pass</Button>
            ) : null}
          </div>
        }
      />

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <div className="grid gap-4 lg:grid-cols-[240px_1fr]">
        <Panel className="text-center">
          {visitor.photoUrl ? (
            <SecureImage
              src={visitor.photoUrl}
              alt={visitor.visitorName}
              className="mx-auto h-48 w-48 rounded-lg object-cover"
            />
          ) : (
            <div className="mx-auto flex h-48 w-48 items-center justify-center rounded-lg bg-gray-100 text-gray-400">No photo</div>
          )}
          <div className="mt-3">
            <Badge className={statusBadgeClass(visitor.statusLabel)}>{visitor.statusLabel}</Badge>
          </div>
          {visitor.isLongStay ? <div className="mt-2 text-sm font-semibold text-danger">Long stay</div> : null}
        </Panel>

        <div className="space-y-4">
          <Panel>
            <h2 className="mb-3 font-semibold text-steel">Visit details</h2>
            <dl className="grid gap-2 text-sm sm:grid-cols-2">
              <Item label="Visitor #" value={visitor.visitorNumber} />
              <Item label="Visit #" value={visitor.visitNumber} />
              <Item label="Visit date" value={formatDate(visitor.visitDate)} />
              <Item label="Host" value={visitor.hostName} />
              <Item label="Department" value={visitor.departmentName} />
              <Item label="Phone" value={visitor.phone || '—'} />
              <Item label="Email" value={visitor.email || '—'} />
              <Item label="Intercom" value={visitor.intercom || '—'} />
              <Item label="Check-in" value={formatDateTime(visitor.checkInAt)} />
              <Item label="Check-out" value={formatDateTime(visitor.checkOutAt)} />
              <Item label="Duration" value={formatDuration(visitor.durationMinutes)} />
              <Item label="Entry / Exit" value={`${visitor.entryGate || '—'} / ${visitor.exitGate || '—'}`} />
              <Item label="Purposes" value={visitor.purposes.join(', ') || '—'} />
              {visitor.purposeNotes ? (
                <Item label="Other purpose detail" value={visitor.purposeNotes} />
              ) : null}
              <Item label="Locations" value={visitor.locations.join(', ') || '—'} />
              <Item label="Plant" value={visitor.plantNumber || '—'} />
              <Item label="Other location" value={visitor.otherLocationText || '—'} />
              <Item label="ID" value={`${visitor.idTypeName || '—'} ${visitor.idNumberMasked || ''}`} />
              <Item label="Notes" value={visitor.notes || '—'} />
            </dl>

            {canGate ? (
              <div className="mt-4 grid gap-3 border-t border-gray-100 pt-4 sm:grid-cols-2">
                <div>
                  <FieldLabel>Entry gate</FieldLabel>
                  <TextSelect value={entryGateId} onChange={(e) => setEntryGateId(e.target.value)}>
                    <option value="">Default</option>
                    {entryGates.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
                  </TextSelect>
                </div>
                <div>
                  <FieldLabel>Exit gate</FieldLabel>
                  <TextSelect value={exitGateId} onChange={(e) => setExitGateId(e.target.value)}>
                    <option value="">Default</option>
                    {exitGates.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
                  </TextSelect>
                </div>
              </div>
            ) : null}
          </Panel>

          <Panel>
            <h2 className="mb-3 font-semibold text-steel">Approval history</h2>
            {visitor.approvalHistory?.length ? (
              <ul className="space-y-2 text-sm">
                {visitor.approvalHistory.map((a) => (
                  <li key={a.id} className="rounded-md bg-gray-50 px-3 py-2">
                    {String(a.status)} · {a.actionBy || '—'} · {formatDateTime(a.actionAt || a.createdAt)}
                    {a.rejectionReason ? <div className="text-danger">{a.rejectionReason}</div> : null}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-gray-500">No approval history.</p>
            )}
          </Panel>
        </div>
      </div>
    </div>
  )
}

function Item({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-gray-500">{label}</dt>
      <dd className="font-medium text-gray-900">{value}</dd>
    </div>
  )
}
