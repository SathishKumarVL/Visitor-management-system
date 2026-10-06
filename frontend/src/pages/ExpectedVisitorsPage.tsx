import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, visitorsApi } from '../lib/api'
import type {
  EmployeeDto,
  ExpectedVisitorRequest,
  MasterItemDto,
  VisitorListItemDto,
  VisitorPhoneLookupDto,
} from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, SelectChip } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'
import { formatDate, isPastDateTime, nextBookingSlot, nowIsoTime, sanitizePhoneDigits, statusBadgeClass, todayIsoDate, validateOptionalTenDigitPhone, validateVisitorEmail } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { hasAnyRole } from '../lib/utils'
import { EmployeeSearchField } from '../components/EmployeeSearchField'

const PAST_APPOINTMENT_ERROR = 'Appointment date and time cannot be in the past.'

const blankForm = (locationId = '') => {
  const slot = nextBookingSlot()
  return {
    visitorName: '',
    companyName: '',
    gender: '' as '' | 1 | 2,
    phone: '',
    email: '',
    expectedDate: slot.date,
    expectedTime: slot.time,
    locationId,
    departmentId: '',
    hostEmployeeId: '',
    hostName: '',
    purposeIds: [] as string[],
    othersChecked: false,
    otherPurposeText: '',
  }
}

const isOthers = (p: MasterItemDto) => p.name.trim().toLowerCase() === 'others'

const duplicateBookingMessage = (hostName: string, date: string, time: string | null | undefined, visitNumber: string) =>
  `This visitor already has an appointment with ${hostName} on ${formatDate(date)}${time ? ` at ${time.slice(0, 5)}` : ''} (booking ${visitNumber}).`

export function ExpectedVisitorsPage() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const userLocationId = useAuthStore((s) => s.user?.locationId || '')
  const canCreate = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Host'])
  const canArrive = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Security'])

  const [date, setDate] = useState(todayIsoDate())
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [showForm, setShowForm] = useState(searchParams.get('book') === '1')
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [purposes, setPurposes] = useState<MasterItemDto[]>([])
  const [employees, setEmployees] = useState<EmployeeDto[]>([])
  const [form, setForm] = useState(() => blankForm(userLocationId))
  const [saving, setSaving] = useState(false)
  const [emailTouched, setEmailTouched] = useState(false)
  const [phoneLookup, setPhoneLookup] = useState<VisitorPhoneLookupDto | null>(null)

  const { pageItems, pagination } = usePagination(items, 12, date)

  const lookupPhone = sanitizePhoneDigits(form.phone)
  const duplicateBooking = phoneLookup?.openBookings.find(
    (b) => b.date === form.expectedDate && !!form.hostEmployeeId && b.hostEmployeeId === form.hostEmployeeId,
  )

  useEffect(() => {
    if (lookupPhone.length !== 10) {
      setPhoneLookup(null)
      return
    }
    let cancelled = false
    const timer = window.setTimeout(() => {
      visitorsApi
        .lookupByPhone(lookupPhone)
        .then((found) => {
          if (cancelled) return
          setPhoneLookup(found)
          if (!found) return
          setForm((f) => ({
            ...f,
            visitorName: found.visitorName,
            companyName: found.companyName,
            gender: found.gender === 1 || found.gender === 2 ? found.gender : f.gender,
            email: found.email?.trim() || f.email,
          }))
        })
        .catch(() => {
          if (!cancelled) setPhoneLookup(null)
        })
    }, 350)
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [lookupPhone])

  const departmentName = useMemo(
    () => departments.find((d) => d.id === form.departmentId)?.name || '',
    [departments, form.departmentId],
  )
  const othersPurpose = useMemo(() => purposes.find(isOthers), [purposes])
  const listedPurposes = useMemo(() => purposes.filter((p) => !isOthers(p)), [purposes])

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
    if (!canCreate) return
    if (userLocationId) {
      setForm((f) => (f.locationId === userLocationId ? f : { ...f, locationId: userLocationId, departmentId: '', hostEmployeeId: '', hostName: '' }))
    }
  }, [canCreate, userLocationId])

  useEffect(() => {
    if (!canCreate) {
      setDepartments([])
      setPurposes([])
      setEmployees([])
      return
    }
    const loc = form.locationId || undefined
    void Promise.all([
      mastersApi.departments(true, loc),
      mastersApi.hosts(undefined, true, loc),
      mastersApi.purposes(true, loc),
    ]).then(([d, e, p]) => {
      setDepartments(d)
      setEmployees(e)
      setPurposes(p)
      setForm((f) => ({ ...f, purposeIds: f.purposeIds.filter((id) => p.some((x) => x.id === id)) }))
    }).catch((err) => setError(apiErrorMessage(err)))
  }, [canCreate, form.locationId])

  async function createExpected(e: FormEvent) {
    e.preventDefault()
    const emailError = validateVisitorEmail(form.email)
    if (emailError) {
      setEmailTouched(true)
      setError(emailError)
      return
    }
    if (!form.hostEmployeeId) {
      setError('Select the employee to meet.')
      return
    }
    if (!form.departmentId) {
      setError('Department could not be determined for the selected employee.')
      return
    }
    if (form.gender !== 1 && form.gender !== 2) {
      setError('Please select Mr. or Ms.')
      return
    }
    if (isPastDateTime(form.expectedDate, form.expectedTime)) {
      setError(PAST_APPOINTMENT_ERROR)
      return
    }
    if (form.purposeIds.length === 0 && !form.othersChecked) {
      setError('Select at least one purpose.')
      return
    }
    if (form.othersChecked && !form.otherPurposeText.trim()) {
      setError('Please enter the specific reason for Others.')
      return
    }
    if (!sanitizePhoneDigits(form.phone)) {
      setError('Mobile number is required — reception uses it to find the booking when the visitor arrives.')
      return
    }
    const phoneCheck = validateOptionalTenDigitPhone(form.phone)
    if (!phoneCheck.ok) {
      setError(phoneCheck.error)
      return
    }
    if (duplicateBooking) {
      setError(duplicateBookingMessage(duplicateBooking.hostName, duplicateBooking.date, duplicateBooking.time, duplicateBooking.visitNumber))
      return
    }
    setSaving(true)
    setMessage(null)
    setError(null)
    const body: ExpectedVisitorRequest = {
      visitorName: form.visitorName.trim(),
      companyName: form.companyName.trim(),
      gender: form.gender,
      phone: phoneCheck.phone,
      email: form.email.trim(),
      expectedDate: form.expectedDate,
      expectedTime: `${form.expectedTime}:00`,
      departmentId: form.departmentId,
      hostEmployeeId: form.hostEmployeeId,
      hostName: form.hostName.trim() || null,
      purposeIds: form.othersChecked && othersPurpose ? [...form.purposeIds, othersPurpose.id] : form.purposeIds,
      purposeNotes: form.othersChecked ? form.otherPurposeText.trim() : null,
      locationIds: form.locationId ? [form.locationId] : [],
    }
    try {
      await visitorsApi.createExpected(body)
      const bookedDate = form.expectedDate
      setMessage(`Appointment booked for ${formatDate(bookedDate)}. It now appears in the expected visitors grid.`)
      setShowForm(false)
      setSearchParams({})
      setForm(blankForm(userLocationId))
      if (bookedDate !== date) setDate(bookedDate)
      else await load()
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
      {saving ? <LoadingMask label="Saving appointment…" /> : null}
      {loading ? <Spinner label="Loading expected visitors…" /> : null}
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
              <FieldLabel>Mobile *</FieldLabel>
              <TextInput
                required
                inputMode="numeric"
                autoComplete="tel-national"
                placeholder="10-digit number — existing visitors load automatically"
                maxLength={10}
                value={form.phone}
                onChange={(e) => setForm({ ...form, phone: sanitizePhoneDigits(e.target.value) })}
              />
            </div>
            <div className="hidden sm:block" />
            {phoneLookup ? (
              <div className="sm:col-span-2 space-y-2 rounded-xl border border-primary/30 bg-mint/30 p-3 text-sm">
                <p className="text-ink">
                  <span className="font-semibold">Existing visitor: {phoneLookup.visitorName}</span>
                  {phoneLookup.companyName ? ` · ${phoneLookup.companyName}` : ''} ({phoneLookup.visitorNumber})
                  {phoneLookup.totalVisits > 0
                    ? ` · ${phoneLookup.totalVisits} previous visit${phoneLookup.totalVisits === 1 ? '' : 's'}`
                    : ''}
                  {phoneLookup.lastVisitDate ? `, last on ${formatDate(phoneLookup.lastVisitDate)}` : ''}. Details
                  loaded below.
                </p>
                {phoneLookup.openBookings.length > 0 ? (
                  <div>
                    <p className="font-medium text-ink">Already booked:</p>
                    <ul className="mt-1 list-disc space-y-0.5 pl-5 text-ink-muted">
                      {phoneLookup.openBookings.map((b) => (
                        <li key={b.visitId}>
                          {formatDate(b.date)}
                          {b.time ? ` at ${b.time.slice(0, 5)}` : ''} with {b.hostName} ({b.visitNumber})
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </div>
            ) : null}
            {duplicateBooking ? (
              <div className="sm:col-span-2">
                <Alert tone="error">
                  {duplicateBookingMessage(
                    duplicateBooking.hostName,
                    duplicateBooking.date,
                    duplicateBooking.time,
                    duplicateBooking.visitNumber,
                  )}{' '}
                  Choose a different date or person to meet.
                </Alert>
              </div>
            ) : null}
            <div>
              <FieldLabel>Visitor name *</FieldLabel>
              <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                <div className="flex shrink-0 gap-2">
                  <Button
                    type="button"
                    variant={form.gender === 1 ? 'primary' : 'secondary'}
                    onClick={() => setForm({ ...form, gender: 1 })}
                  >
                    Mr.
                  </Button>
                  <Button
                    type="button"
                    variant={form.gender === 2 ? 'primary' : 'secondary'}
                    onClick={() => setForm({ ...form, gender: 2 })}
                  >
                    Ms.
                  </Button>
                </div>
                <TextInput
                  required
                  className="min-w-0 flex-1"
                  value={form.visitorName}
                  onChange={(e) => setForm({ ...form, visitorName: e.target.value })}
                />
              </div>
              {form.gender !== 1 && form.gender !== 2 ? (
                <p className="mt-1 text-xs font-medium text-danger">Please select</p>
              ) : null}
            </div>
            <div>
              <FieldLabel>Company *</FieldLabel>
              <TextInput required value={form.companyName} onChange={(e) => setForm({ ...form, companyName: e.target.value })} />
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
              <TextInput
                type="date"
                required
                min={todayIsoDate()}
                value={form.expectedDate}
                onChange={(e) => setForm({ ...form, expectedDate: e.target.value })}
              />
            </div>
            <div>
              <FieldLabel>Time *</FieldLabel>
              <TextInput
                type="time"
                required
                min={form.expectedDate === todayIsoDate() ? nowIsoTime() : undefined}
                value={form.expectedTime}
                onChange={(e) => setForm({ ...form, expectedTime: e.target.value })}
              />
              {isPastDateTime(form.expectedDate, form.expectedTime) ? (
                <p className="mt-1 text-xs text-danger">{PAST_APPOINTMENT_ERROR}</p>
              ) : null}
            </div>
            <div>
              <EmployeeSearchField
                employees={employees}
                value={form.hostEmployeeId}
                onSelect={(emp) => {
                  if (!emp) {
                    setForm({ ...form, hostEmployeeId: '', hostName: '', departmentId: '' })
                    return
                  }
                  setForm({
                    ...form,
                    hostEmployeeId: emp.id,
                    hostName: emp.fullName,
                    departmentId: emp.departmentId,
                  })
                }}
              />
            </div>
            <div>
              <FieldLabel>Department</FieldLabel>
              <TextInput
                readOnly
                className="bg-gray-50 text-gray-700"
                value={departmentName || (form.hostEmployeeId ? '—' : 'Select employee first')}
              />
            </div>
            <div className="sm:col-span-2">
              <FieldLabel>Purpose *</FieldLabel>
              <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
                {listedPurposes.map((p) => (
                  <SelectChip
                    key={p.id}
                    selected={form.purposeIds.includes(p.id)}
                    onToggle={() =>
                      setForm({
                        ...form,
                        purposeIds: form.purposeIds.includes(p.id)
                          ? form.purposeIds.filter((id) => id !== p.id)
                          : [...form.purposeIds, p.id],
                      })
                    }
                  >
                    {p.name}
                  </SelectChip>
                ))}
                <SelectChip
                  selected={form.othersChecked}
                  onToggle={() =>
                    setForm({
                      ...form,
                      othersChecked: !form.othersChecked,
                      otherPurposeText: form.othersChecked ? '' : form.otherPurposeText,
                    })
                  }
                >
                  Others
                </SelectChip>
              </div>
              {form.othersChecked ? (
                <div className="mt-3">
                  <FieldLabel htmlFor="expectedOtherPurpose">Specific reason *</FieldLabel>
                  <TextInput
                    id="expectedOtherPurpose"
                    maxLength={500}
                    placeholder="Describe the purpose of visit"
                    value={form.otherPurposeText}
                    onChange={(e) => setForm({ ...form, otherPurposeText: e.target.value })}
                  />
                </div>
              ) : null}
            </div>
            <div className="sm:col-span-2">
              <Button type="submit" disabled={saving || !!duplicateBooking}>{saving ? 'Saving…' : 'Save appointment'}</Button>
            </div>
          </form>
        </Panel>
      ) : null}

      {!loading && items.length === 0 ? (
        <EmptyState title="No expected visitors for this date" description="Book an appointment from Reception Mode or use Book appointment above." />
      ) : null}

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {pageItems.map((v) => (
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
            <div className="mt-3 space-y-1 text-sm">
              <div><span className="text-gray-500">When:</span> {formatDate(v.visitDate)} {v.visitTime?.slice(0, 5)}</div>
              <div><span className="text-gray-500">Person to meet:</span> {v.hostName}</div>
              <div><span className="text-gray-500">Dept:</span> {v.departmentName}</div>
              {v.purposes?.length ? (
                <div className="truncate"><span className="text-gray-500">Purpose:</span> {v.purposes.join(', ')}</div>
              ) : null}
            </div>
          </button>
        ))}
      </div>
      {!loading && items.length > 0 ? (
        <Panel className="mt-3 p-0">
          <Pagination {...pagination} pageSizeOptions={[12, 24, 48, 96]} bordered={false} />
        </Panel>
      ) : null}
    </div>
  )
}
