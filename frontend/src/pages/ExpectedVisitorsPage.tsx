import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
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
import { TextInput, TextSelect, FieldLabel, SelectChip } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'
import { formatDate, isPastDateTime, nextBookingSlot, sanitizePhoneDigits, statusBadgeClass, todayIsoDate, validateOptionalTenDigitPhone, validateVisitorEmail } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { cn, hasAnyRole } from '../lib/utils'
import { EmployeeSearchField } from '../components/EmployeeSearchField'

const PAST_APPOINTMENT_ERROR = 'Appointment date and time cannot be in the past.'

/** Quarter-hour booking times, 00:00 to 23:45. */
const BOOKING_SLOTS = Array.from({ length: 96 }, (_, i) =>
  `${String(Math.floor(i / 4)).padStart(2, '0')}:${String((i % 4) * 15).padStart(2, '0')}`,
)

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

  const [date, setDate] = useState(() => {
    const fromUrl = searchParams.get('date')
    return fromUrl && /^\d{4}-\d{2}-\d{2}$/.test(fromUrl) ? fromUrl : todayIsoDate()
  })
  const [savedVisitId, setSavedVisitId] = useState<string | null>(null)
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [loadedDate, setLoadedDate] = useState<string | null>(null)
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
  const [editing, setEditing] = useState<{ visitId: string; visitNumber: string; date: string; time: string } | null>(null)

  const { pageItems, pagination } = usePagination(items, 12, date)

  // Notification links carry ?date=…&visit=…; they can arrive while this page is already open.
  const focusVisitId = searchParams.get('visit')
  const urlDate = searchParams.get('date')
  useEffect(() => {
    if (urlDate && /^\d{4}-\d{2}-\d{2}$/.test(urlDate)) setDate(urlDate)
  }, [urlDate])

  const focusIndex = focusVisitId ? items.findIndex((v) => v.visitId === focusVisitId) : -1
  const focusPage = focusIndex >= 0 ? Math.floor(focusIndex / pagination.pageSize) + 1 : null
  const onPageChange = pagination.onPageChange
  useEffect(() => {
    if (focusPage) onPageChange(focusPage)
  }, [focusPage, onPageChange])

  const focusOnPage = !!focusVisitId && pageItems.some((v) => v.visitId === focusVisitId)
  useEffect(() => {
    if (focusOnPage) document.getElementById(`booking-${focusVisitId}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }, [focusOnPage, focusVisitId])

  const lookupPhone = sanitizePhoneDigits(form.phone)
  const duplicateBooking = phoneLookup?.openBookings.find(
    (b) =>
      b.visitId !== editing?.visitId &&
      b.date === form.expectedDate &&
      !!form.hostEmployeeId &&
      b.hostEmployeeId === form.hostEmployeeId,
  )
  // An edit that keeps the booked slot may save even after that time has passed today.
  const keepsBookedSlot = !!editing && form.expectedDate === editing.date && form.expectedTime === editing.time
  const slotInPast = !keepsBookedSlot && isPastDateTime(form.expectedDate, form.expectedTime)

  // Re-render every minute while booking so slots that have just passed become disabled.
  const [, setMinuteTick] = useState(0)
  useEffect(() => {
    if (!showForm) return
    const timer = window.setInterval(() => setMinuteTick((n) => n + 1), 60_000)
    return () => window.clearInterval(timer)
  }, [showForm])

  // An existing booking at an odd time (e.g. 10:07) keeps its own option.
  const timeSlots = (
    !form.expectedTime || BOOKING_SLOTS.includes(form.expectedTime)
      ? BOOKING_SLOTS
      : [...BOOKING_SLOTS, form.expectedTime].sort()
  ).map((time) => ({
    time,
    disabled:
      !(editing && form.expectedDate === editing.date && time === editing.time) &&
      isPastDateTime(form.expectedDate, time),
  }))

  /** Moving to today (or a typed past date) jumps the time to the next open slot. */
  function changeExpectedDate(value: string) {
    const keeps = !!editing && value === editing.date && form.expectedTime === editing.time
    if (keeps || !isPastDateTime(value, form.expectedTime)) {
      setForm({ ...form, expectedDate: value })
      return
    }
    const slot = nextBookingSlot()
    setForm({ ...form, expectedDate: value < slot.date ? slot.date : value, expectedTime: slot.time })
  }

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
      setLoadedDate(date)
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
    if (!savedVisitId) return
    const timer = window.setTimeout(() => setSavedVisitId(null), 6000)
    return () => window.clearTimeout(timer)
  }, [savedVisitId])

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
    if (slotInPast) {
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
      const saved = editing
        ? await visitorsApi.updateExpected(editing.visitId, body)
        : await visitorsApi.createExpected(body)
      const bookedDate = form.expectedDate
      setMessage(editing ? `Appointment updated for ${formatDate(bookedDate)}.` : `Appointment booked for ${formatDate(bookedDate)}.`)
      closeForm({ date: bookedDate })
      setSavedVisitId(saved.visitId)
      window.scrollTo({ top: 0, behavior: 'smooth' })
      if (bookedDate !== date) setDate(bookedDate)
      else await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  function closeForm(params: Record<string, string> = { date }) {
    setShowForm(false)
    setEditing(null)
    setSearchParams(params, { replace: true })
    setForm(blankForm(userLocationId))
    setEmailTouched(false)
  }

  async function startEdit(visitId: string) {
    setError(null)
    setMessage(null)
    setSaving(true)
    try {
      const v = await visitorsApi.get(visitId)
      const othersId = othersPurpose?.id
      const time = (v.visitTime || '').slice(0, 5)
      setEditing({ visitId: v.visitId, visitNumber: v.visitNumber, date: v.visitDate, time })
      setForm({
        visitorName: v.visitorName,
        companyName: v.companyName,
        gender: v.gender === 1 || v.gender === 2 ? v.gender : '',
        phone: sanitizePhoneDigits(v.phone || ''),
        email: (v.email || '').trim(),
        expectedDate: v.visitDate,
        expectedTime: time,
        locationId: v.locationIds?.[0] || userLocationId,
        departmentId: v.departmentId || '',
        hostEmployeeId: v.hostEmployeeId || '',
        hostName: v.hostName || '',
        purposeIds: (v.purposeIds ?? []).filter((id) => id !== othersId),
        othersChecked: !!v.purposeNotes?.trim() || (!!othersId && (v.purposeIds ?? []).includes(othersId)),
        otherPurposeText: v.purposeNotes || '',
      })
      setShowForm(true)
      window.scrollTo({ top: 0, behavior: 'smooth' })
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
            <TextInput
              type="date"
              value={date}
              onChange={(e) => {
                setDate(e.target.value)
                setSearchParams(
                  (prev) => {
                    const next = new URLSearchParams(prev)
                    next.set('date', e.target.value)
                    next.delete('visit')
                    return next
                  },
                  { replace: true },
                )
              }}
              className="w-auto"
            />
            {canCreate ? (
              <Button
                variant="amber"
                onClick={() => {
                  if (showForm) {
                    closeForm()
                    return
                  }
                  setShowForm(true)
                  setSearchParams({ book: '1', date })
                }}
              >
                {showForm ? (editing ? 'Cancel edit' : 'Hide booking form') : 'Book appointment'}
              </Button>
            ) : null}
          </div>
        }
      />

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}
      {focusVisitId && !loading && focusIndex < 0 && loadedDate === date && urlDate === date ? (
        <div className="mb-4">
          <Alert tone="info" persistent>
            This appointment is no longer waiting: the visitor may have already arrived, or it was cancelled.{' '}
            <Link to={`/visitors/${focusVisitId}`} className="font-medium text-primary underline">
              Open the visit
            </Link>
          </Alert>
        </div>
      ) : null}

      {showForm ? (
        <Panel className="mb-4">
          <h2 className="mb-3 text-sm font-semibold text-steel">
            {editing ? `Edit appointment ${editing.visitNumber}` : 'Book appointment'}
          </h2>
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
                <Alert tone="error" persistent>
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
                min={editing && editing.date < todayIsoDate() ? editing.date : todayIsoDate()}
                value={form.expectedDate}
                onChange={(e) => changeExpectedDate(e.target.value)}
              />
            </div>
            <div>
              <FieldLabel htmlFor="expectedTime">Time *</FieldLabel>
              <TextSelect
                id="expectedTime"
                required
                value={form.expectedTime}
                onChange={(e) => setForm({ ...form, expectedTime: e.target.value })}
              >
                {timeSlots.map((s) => (
                  <option
                    key={s.time}
                    value={s.time}
                    disabled={s.disabled}
                    style={
                      s.disabled
                        ? { color: '#9CA3AF', backgroundColor: '#F3F4F6' }
                        : { color: '#047857', fontWeight: 600 }
                    }
                  >
                    {s.time}
                  </option>
                ))}
              </TextSelect>
              <p className="mt-1 text-xs text-ink-muted">
                <span className="font-semibold text-emerald-700">Green</span> times are available; grey times have
                passed.
              </p>
              {slotInPast ? (
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
            <div className="flex flex-wrap gap-2 sm:col-span-2">
              <Button type="submit" disabled={saving || !!duplicateBooking || slotInPast}>
                {saving ? 'Saving…' : editing ? 'Save changes' : 'Save appointment'}
              </Button>
              {editing ? (
                <Button type="button" variant="secondary" onClick={() => closeForm()}>
                  Cancel
                </Button>
              ) : null}
            </div>
          </form>
        </Panel>
      ) : null}

      {!loading && items.length === 0 ? (
        <EmptyState title="No expected visitors for this date" description="Book an appointment from Reception Mode or use Book appointment above." />
      ) : null}

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {pageItems.map((v) => {
          const canEdit = canCreate && String(v.statusLabel).toLowerCase() === 'expected'
          return (
            <div
              key={v.visitId}
              id={`booking-${v.visitId}`}
              className={cn(
                'flex flex-col rounded-lg border border-gray-200 bg-white shadow-sm transition hover:border-steel hover:shadow-md',
                (v.visitId === savedVisitId || v.visitId === focusVisitId) && 'border-primary ring-2 ring-primary/40',
              )}
            >
              <button
                type="button"
                disabled={!canArrive}
                onClick={() => openArrival(v.visitId)}
                className="flex-1 rounded-lg p-4 text-left disabled:cursor-not-allowed disabled:opacity-60"
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
              {canEdit ? (
                <div className="flex justify-end border-t border-gray-100 px-4 py-2">
                  <Button size="sm" variant="secondary" onClick={() => void startEdit(v.visitId)}>
                    Edit
                  </Button>
                </div>
              ) : null}
            </div>
          )
        })}
      </div>
      {!loading && items.length > 0 ? (
        <Panel className="mt-3 p-0">
          <Pagination {...pagination} pageSizeOptions={[12, 24, 48, 96]} bordered={false} />
        </Panel>
      ) : null}
    </div>
  )
}
