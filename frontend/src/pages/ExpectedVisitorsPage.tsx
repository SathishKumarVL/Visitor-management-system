import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, visitorsApi } from '../lib/api'
import type { ExpectedVisitorRequest, MasterItemDto, VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { formatDate, nowIsoTime, statusBadgeClass, todayIsoDate, validateVisitorEmail } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { hasAnyRole } from '../lib/utils'

export function ExpectedVisitorsPage() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const canCreate = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Host'])
  const canArrive = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Security'])

  const [date, setDate] = useState(todayIsoDate())
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [showForm, setShowForm] = useState(searchParams.get('book') === '1')
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [form, setForm] = useState({
    visitorName: '',
    companyName: '',
    phone: '',
    email: '',
    expectedDate: todayIsoDate(),
    expectedTime: nowIsoTime(),
    departmentId: '',
    hostName: '',
  })
  const [saving, setSaving] = useState(false)
  const [emailTouched, setEmailTouched] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await visitorsApi.expected(date))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [date])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    if (canCreate) void mastersApi.departments().then(setDepartments)
  }, [canCreate])

  async function createExpected(e: FormEvent) {
    e.preventDefault()
    const emailError = validateVisitorEmail(form.email)
    if (emailError) {
      setEmailTouched(true)
      setError(emailError)
      return
    }
    if (!form.hostName.trim()) {
      setError('Enter the person to meet.')
      return
    }
    setSaving(true)
    setMessage(null)
    setError(null)
    const body: ExpectedVisitorRequest = {
      visitorName: form.visitorName.trim(),
      companyName: form.companyName.trim(),
      phone: form.phone || null,
      email: form.email.trim(),
      expectedDate: form.expectedDate,
      expectedTime: `${form.expectedTime}:00`,
      departmentId: form.departmentId,
      hostName: form.hostName.trim(),
      purposeIds: [],
      locationIds: [],
    }
    try {
      await visitorsApi.createExpected(body)
      setMessage('Appointment booked. It now appears in the expected visitors grid.')
      setShowForm(false)
      setSearchParams({})
      setForm({
        visitorName: '',
        companyName: '',
        phone: '',
        email: '',
        expectedDate: todayIsoDate(),
        expectedTime: nowIsoTime(),
        departmentId: '',
        hostName: '',
      })
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  function openArrival(visitId: string) {
    navigate(`/visitors/new?expectedId=${visitId}`)
  }

  return (
    <div>
      <PageHeader
        title="Expected Visitors"
        subtitle="Appointments booked at reception — tap a card to check in with face capture"
        actions={
          <div className="flex flex-wrap gap-2">
            <TextInput type="date" value={date} onChange={(e) => setDate(e.target.value)} className="w-auto" />
            {canCreate ? (
              <Button
                variant="amber"
                onClick={() => {
                  setShowForm((v) => !v)
                  if (!showForm) setSearchParams({ book: '1' })
                  else setSearchParams({})
                }}
              >
                {showForm ? 'Hide booking form' : 'Book appointment'}
              </Button>
            ) : null}
          </div>
        }
      />

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      {showForm ? (
        <Panel className="mb-4">
          <h2 className="mb-3 text-sm font-semibold text-steel">Book appointment</h2>
          <form onSubmit={createExpected} className="grid gap-3 sm:grid-cols-2">
            <div>
              <FieldLabel>Visitor name *</FieldLabel>
              <TextInput required value={form.visitorName} onChange={(e) => setForm({ ...form, visitorName: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Company *</FieldLabel>
              <TextInput required value={form.companyName} onChange={(e) => setForm({ ...form, companyName: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Phone</FieldLabel>
              <TextInput value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Email *</FieldLabel>
              <TextInput
                type="email"
                required
                value={form.email}
                onChange={(e) => setForm({ ...form, email: e.target.value.replace(/\s/g, '') })}
                onBlur={() => setEmailTouched(true)}
              />
              {emailTouched && validateVisitorEmail(form.email) ? (
                <p className="mt-1 text-xs text-danger">{validateVisitorEmail(form.email)}</p>
              ) : null}
            </div>
            <div>
              <FieldLabel>Date *</FieldLabel>
              <TextInput type="date" required value={form.expectedDate} onChange={(e) => setForm({ ...form, expectedDate: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Time *</FieldLabel>
              <TextInput type="time" required value={form.expectedTime} onChange={(e) => setForm({ ...form, expectedTime: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Department *</FieldLabel>
              <TextSelect required value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })}>
                <option value="">Select</option>
                {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
              </TextSelect>
            </div>
            <div>
              <FieldLabel>Person to meet *</FieldLabel>
              <TextInput
                required
                value={form.hostName}
                onChange={(e) => setForm({ ...form, hostName: e.target.value })}
                placeholder="Type the person to meet"
              />
            </div>
            <div className="sm:col-span-2">
              <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save appointment'}</Button>
            </div>
          </form>
        </Panel>
      ) : null}

      {loading ? <Spinner /> : null}
      {!loading && items.length === 0 ? (
        <EmptyState title="No expected visitors for this date" description="Book an appointment from Reception Mode or use Book appointment above." />
      ) : null}

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {items.map((v) => (
          <button
            key={v.visitId}
            type="button"
            disabled={!canArrive}
            onClick={() => openArrival(v.visitId)}
            className="rounded-lg border border-gray-200 bg-white p-4 text-left shadow-sm transition hover:border-steel hover:shadow-md disabled:cursor-not-allowed disabled:opacity-60"
          >
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <div className="truncate text-lg font-semibold text-steel">{v.visitorName}</div>
                <div className="truncate text-sm text-gray-600">{v.companyName}</div>
              </div>
              <Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge>
            </div>
            <dl className="mt-3 space-y-1 text-sm text-gray-600">
              <div><span className="text-gray-500">Person to meet:</span> {v.hostName}</div>
              <div><span className="text-gray-500">Dept:</span> {v.departmentName}</div>
              <div><span className="text-gray-500">When:</span> {formatDate(v.visitDate)} · {String(v.visitTime).slice(0, 5)}</div>
              {v.phone ? <div><span className="text-gray-500">Phone:</span> {v.phone}</div> : null}
            </dl>
            {canArrive ? (
              <div className="mt-4 text-xs font-semibold uppercase tracking-wide text-amber-dark">
                Tap to check in · capture face
              </div>
            ) : null}
          </button>
        ))}
      </div>
    </div>
  )
}
