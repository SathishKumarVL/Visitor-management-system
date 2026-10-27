import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, lookupsApi, mastersApi, passApi, visitorsApi } from '../lib/api'
import type {
  EmployeeDto,
  LookupMatchDto,
  LookupRecordVisitorsDto,
  LookupSourceOptionDto,
  MasterItemDto,
  PreviousPurposeAnswersDto,
  RegisterVisitorRequest,
  VisitorWizardDraft,
} from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldError, TextInput, FieldLabel, TextSelect, SelectChip } from '../components/ui/Field'
import {
  formatDateTime,
  newUuid,
  nowIsoTime,
  sanitizePhoneDigits,
  todayIsoDate,
  validateVisitorEmail,
} from '../lib/utils'
import { useOnlineStatus } from '../hooks/useOnlineStatus'
import { newPersonAlertText, speakAlert } from '../lib/alertSound'
import { SecureImage } from '../components/SecureImage'
import { LoadingMask } from '../components/LoadingMask'
import { FaceCameraPanel } from '../components/FaceCameraPanel'
import { captureFaceJpeg, openFaceCamera, toggleCameraFacing } from '../lib/cameraCapture'
import type { BookingFaceCheckDto, BookingMatchDto, FaceSearchMatchDto, GroupEmployeeMatchDto, PassDto } from '../types/api'
import { useAuthStore } from '../store/authStore'
import { EmployeeSearchField } from '../components/EmployeeSearchField'
import { LookupFieldInput, type LookupFieldNote } from '../components/LookupFieldInput'

const LEGACY_DRAFT_KEY = 'tiaano_visitor_wizard_draft'

const PREVIOUS_NOTES = {
  checking: { tone: 'muted', text: 'From the last visit. Checking it is still on record…' },
  confirmed: { tone: 'ok', text: '✓ From the last visit, still on record' },
  notFound: {
    tone: 'warn',
    text: 'From the last visit, but not found with these details now (for example, a new class). Correct the details and search again, or it is saved as not verified.',
  },
  unavailable: {
    tone: 'warn',
    text: 'From the last visit. It could not be checked right now: search again to verify it, or it is saved as not verified.',
  },
  unchecked: {
    tone: 'warn',
    text: 'From the last visit. Search and pick it again to verify it, or it is saved as not verified.',
  },
} satisfies Record<string, LookupFieldNote>

/** "Parent visit: RAKSHAN - Class 1st-A": lookup picks name the entry best, otherwise all answers. */
function previousAnswersLabel(set: PreviousPurposeAnswersDto): string {
  const picked = set.fields.filter((f) => f.lookupSourceId).map((f) => f.value)
  return `${set.purposeName}: ${(picked.length > 0 ? picked : set.fields.map((f) => f.value)).join(' · ')}`
}
const STATIC_ID_TYPES = ['Aadhaar', 'Pan Card', 'Passport'] as const

const emptyDraft = (): VisitorWizardDraft => ({
  step: 0,
  visitorName: '',
  gender: '',
  telephone: '',
  email: '',
  companyName: '',
  visitDate: todayIsoDate(),
  visitTime: nowIsoTime(),
  numberOfPersons: 1,
  departmentId: '',
  hostName: '',
  hostEmployeeId: '',
  purposeIds: [],
  othersChecked: false,
  locationIds: [],
  locationId: '',
  plantNumber: '',
  otherLocationText: '',
  otherPurposeText: '',
  purposeFieldValues: {},
  purposeLookupPicks: {},
  notes: '',
  photoBase64: '',
  idTypeName: '',
  idNumber: '',
  isWalkIn: true,
  expectedVisitId: null,
  recognizedVisitorId: null,
  groupEmployeeUserId: null,
  groupEmployeeId: null,
  passNumber: '',
})

function toggleId(list: string[], id: string): string[] {
  return list.includes(id) ? list.filter((x) => x !== id) : [...list, id]
}

function SectionTitle({ children }: { children: ReactNode }) {
  return (
    <h2 className="mb-3 text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
      {children}
    </h2>
  )
}

export function NewVisitorWizardPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const expectedId = searchParams.get('expectedId')
  const isArrival = !!expectedId
  const online = useOnlineStatus()
  const userLocationId = useAuthStore((s) => s.user?.locationId || '')
  const [draft, setDraft] = useState<VisitorWizardDraft>(() => emptyDraft())
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [purposes, setPurposes] = useState<MasterItemDto[]>([])
  const [employees, setEmployees] = useState<EmployeeDto[]>([])
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [faceStatus, setFaceStatus] = useState<string | null>(null)
  const [faceBusy, setFaceBusy] = useState(false)
  const [faceMatch, setFaceMatch] = useState<FaceSearchMatchDto | null>(null)
  const [employeeMatch, setEmployeeMatch] = useState<GroupEmployeeMatchDto | null>(null)
  /** Earlier visitor record with the same face, kept while the desk confirms the employee match. */
  const [employeeVisitorMatch, setEmployeeVisitorMatch] = useState<FaceSearchMatchDto | null>(null)
  const [emailTouched, setEmailTouched] = useState(false)
  const [loadingExpected, setLoadingExpected] = useState(!!expectedId)
  const [bookingFace, setBookingFace] = useState<BookingFaceCheckDto | null>(null)
  const [faceMismatchConfirmed, setFaceMismatchConfirmed] = useState(false)
  const [bookingMatches, setBookingMatches] = useState<BookingMatchDto[]>([])
  const [dismissedBookingIds, setDismissedBookingIds] = useState<string[]>([])
  const [linkedBooking, setLinkedBooking] = useState<BookingMatchDto | null>(null)
  /** Visitor record of the arriving booking; the visit joins it unless a face match names someone else. */
  const [arrivalVisitorId, setArrivalVisitorId] = useState<string | null>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const formTopRef = useRef<HTMLDivElement>(null)
  /** Sent with every attempt to register this form, so a retry after a timeout returns the saved visit. */
  const clientRequestId = useRef(newUuid())

  const othersPurpose = useMemo(
    () => purposes.find((p) => p.name.trim().toLowerCase() === 'others'),
    [purposes],
  )
  const listedPurposes = useMemo(
    () => purposes.filter((p) => p.name.trim().toLowerCase() !== 'others'),
    [purposes],
  )
  /** Selected purposes that ask extra fields, in master order. */
  const purposesWithFields = useMemo(() => {
    const selected = new Set(draft.purposeIds)
    if (draft.othersChecked && othersPurpose) selected.add(othersPurpose.id)
    return purposes.filter((p) => selected.has(p.id) && (p.fields?.length ?? 0) > 0)
  }, [purposes, draft.purposeIds, draft.othersChecked, othersPurpose])
  const needsLookups = useMemo(
    () => purposes.some((p) => p.fields?.some((f) => f.type === 'lookup')),
    [purposes],
  )
  const [lookupSources, setLookupSources] = useState<LookupSourceOptionDto[]>([])
  useEffect(() => {
    if (needsLookups) void lookupsApi.sources().then(setLookupSources).catch(() => setLookupSources([]))
  }, [needsLookups])
  const departmentName = useMemo(
    () => departments.find((d) => d.id === draft.departmentId)?.name || '',
    [departments, draft.departmentId],
  )

  /** Details form only after a confirmed capture (match resolved or none). */
  const showDetailsForm = !!draft.photoBase64 && !faceMatch && !employeeMatch && !faceBusy
  const showFaceStage = !showDetailsForm
  const returningVisitor = !!draft.recognizedVisitorId
  const lookupPhone = sanitizePhoneDigits(draft.telephone)
  const bookingLookupKey =
    showDetailsForm && !isArrival && !draft.expectedVisitId
      ? `${lookupPhone.length === 10 ? lookupPhone : ''}|${draft.recognizedVisitorId ?? ''}`
      : ''
  const visibleBookingMatches = bookingMatches.filter((m) => !dismissedBookingIds.includes(m.visitId))

  function syncOthersPurposeIds(ids: string[], othersChecked: boolean): string[] {
    if (!othersPurpose) return ids
    const without = ids.filter((id) => id !== othersPurpose.id)
    return othersChecked ? [...without, othersPurpose.id] : without
  }

  function hasPurposeSelection(): boolean {
    const standardCount = othersPurpose
      ? draft.purposeIds.filter((id) => id !== othersPurpose.id).length
      : draft.purposeIds.length
    return standardCount > 0 || draft.othersChecked
  }

  useEffect(() => {
    try {
      localStorage.removeItem(LEGACY_DRAFT_KEY)
    } catch {
      /* ignore */
    }
  }, [])

  useEffect(() => {
    return () => {
      streamRef.current?.getTracks().forEach((t) => t.stop())
    }
  }, [])

  // Location comes from login only — never shown as a form field.
  useEffect(() => {
    if (expectedId) return
    setDraft((d) => {
      const nextLoc = userLocationId || ''
      const nextIds = nextLoc ? [nextLoc] : []
      if (d.locationId === nextLoc) return d
      return {
        ...d,
        locationId: nextLoc,
        locationIds: nextIds,
        departmentId: '',
        hostEmployeeId: '',
        hostName: '',
        purposeIds: [],
        othersChecked: false,
        otherPurposeText: '',
      }
    })
  }, [userLocationId, expectedId])

  useEffect(() => {
    const loc = draft.locationId || undefined
    void (async () => {
      try {
        const [d, p, e] = await Promise.all([
          mastersApi.departments(true, loc),
          mastersApi.purposes(true, loc),
          mastersApi.hosts(undefined, true, loc),
        ])
        setDepartments(d)
        setPurposes(p)
        setEmployees(e)
      } catch (err) {
        setError(apiErrorMessage(err))
      }
    })()
  }, [draft.locationId])

  useEffect(() => {
    if (!expectedId) return
    void (async () => {
      try {
        setLoadingExpected(true)
        const visit = await visitorsApi.get(expectedId)
        const locId = userLocationId || ''
        const [d, p, hosts] = await Promise.all([
          mastersApi.departments(true, locId || undefined),
          mastersApi.purposes(true, locId || undefined),
          mastersApi.hosts(undefined, true, locId || undefined),
        ])
        setDepartments(d)
        setPurposes(p)
        setEmployees(hosts)
        const dept = d.find((x) => x.name === visit.departmentName)
        const host = hosts.find((h) => h.fullName === visit.hostName)
        const others = p.find((x) => x.name.trim().toLowerCase() === 'others')
        // The API shows the typed reason in place of "Others".
        const othersChecked = !!others && (visit.purposes.includes(others.name) || !!visit.purposeNotes?.trim())
        const purposeIds = p
          .filter((x) => visit.purposes.includes(x.name) || (othersChecked && x.id === others?.id))
          .map((x) => x.id)
        setDraft({
          ...emptyDraft(),
          visitorName: visit.visitorName,
          gender: visit.gender === 1 || visit.gender === 2 ? visit.gender : '',
          companyName: visit.companyName,
          telephone: sanitizePhoneDigits(visit.phone || ''),
          email: (visit.email || '').trim(),
          visitDate: todayIsoDate(),
          visitTime: nowIsoTime(),
          locationId: locId,
          locationIds: locId ? [locId] : [],
          departmentId: host?.departmentId || dept?.id || '',
          hostName: visit.hostName || '',
          hostEmployeeId: host?.id || '',
          purposeIds,
          othersChecked,
          otherPurposeText: visit.purposeNotes || '',
          isWalkIn: false,
          expectedVisitId: visit.visitId,
        })
        setArrivalVisitorId(visit.visitorId)
      } catch (e) {
        setError(apiErrorMessage(e))
      } finally {
        setLoadingExpected(false)
      }
    })()
  }, [expectedId, userLocationId])

  useEffect(() => {
    if (!othersPurpose || !draft.othersChecked) return
    setDraft((d) => {
      if (d.purposeIds.includes(othersPurpose.id)) return d
      return {
        ...d,
        purposeIds: [...d.purposeIds.filter((id) => id !== othersPurpose.id), othersPurpose.id],
      }
    })
  }, [othersPurpose, draft.othersChecked])

  useEffect(() => {
    const [phone, visitorId] = bookingLookupKey.split('|')
    if (!phone && !visitorId) {
      setBookingMatches([])
      return
    }
    let cancelled = false
    const timer = window.setTimeout(() => {
      visitorsApi
        .matchBookings({ phone: phone || undefined, visitorId: visitorId || undefined })
        .then((matches) => {
          if (!cancelled) setBookingMatches(matches)
        })
        .catch(() => {
          if (!cancelled) setBookingMatches([])
        })
    }, 350)
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [bookingLookupKey])

  const [previousAnswers, setPreviousAnswers] = useState<PreviousPurposeAnswersDto[]>([])
  const [appliedPrevious, setAppliedPrevious] = useState<number | null>(null)
  const [previousNotes, setPreviousNotes] = useState<Record<string, LookupFieldNote>>({})
  const draftRef = useRef(draft)
  useEffect(() => {
    draftRef.current = draft
  })
  /** Bumped on every fill, so a slow check from an earlier fill cannot overwrite a newer one. */
  const previousFillRun = useRef(0)

  const recognizedVisitorId = draft.recognizedVisitorId
  useEffect(() => {
    setPreviousAnswers([])
    setAppliedPrevious(null)
    if (!recognizedVisitorId) return
    let cancelled = false
    visitorsApi
      .previousPurposeAnswers(recognizedVisitorId)
      .then((sets) => {
        if (!cancelled) setPreviousAnswers(sets)
      })
      .catch(() => {
        if (!cancelled) setPreviousAnswers([])
      })
    return () => {
      cancelled = true
    }
  }, [recognizedVisitorId])

  // A returning visitor with no purpose chosen yet gets their latest answers straight away.
  useEffect(() => {
    if (previousAnswers.length === 0 || appliedPrevious !== null) return
    const d = draftRef.current
    if (d.purposeIds.length > 0 || d.othersChecked) return
    applyPreviousAnswers(0)
  }, [previousAnswers, purposes])

  /** Fills a purpose and its details from an earlier visit, then checks each lookup pick is still on record. */
  function applyPreviousAnswers(index: number): boolean {
    const set = previousAnswers[index]
    const purpose = set ? purposes.find((p) => p.id === set.purposeId) : undefined
    if (!set || !purpose) return false
    const fieldIds = new Set((purpose.fields ?? []).flatMap((f) => (f.id ? [f.id] : [])))
    const answers = set.fields.filter((a) => fieldIds.has(a.fieldId))
    const isOthers = othersPurpose?.id === purpose.id
    const run = ++previousFillRun.current

    setDraft((d) => {
      const values = { ...d.purposeFieldValues }
      const picks = { ...d.purposeLookupPicks }
      for (const id of fieldIds) {
        delete values[id]
        delete picks[id]
      }
      for (const a of answers) values[a.fieldId] = a.value
      return {
        ...d,
        purposeIds: d.purposeIds.includes(purpose.id) ? d.purposeIds : [...d.purposeIds, purpose.id],
        othersChecked: d.othersChecked || isOthers,
        purposeFieldValues: values,
        purposeLookupPicks: picks,
      }
    })
    setPreviousNotes((n) => {
      const next = { ...n }
      for (const id of fieldIds) delete next[id]
      for (const a of answers) {
        if (a.lookupSourceId) next[a.fieldId] = a.recordId && a.query ? PREVIOUS_NOTES.checking : PREVIOUS_NOTES.unchecked
      }
      return next
    })
    setAppliedPrevious(index)

    for (const a of answers) {
      if (!a.lookupSourceId || !a.recordId || !a.query) continue
      const context = Object.fromEntries(answers.filter((o) => o.fieldId !== a.fieldId).map((o) => [o.label, o.value]))
      const settle = (note: LookupFieldNote, match?: LookupMatchDto | null) => {
        // Reception may have edited the field or filled another entry while the check ran.
        if (previousFillRun.current !== run || draftRef.current.purposeFieldValues[a.fieldId] !== a.value) return
        if (match?.token) {
          setDraft((d) => ({
            ...d,
            purposeFieldValues: { ...d.purposeFieldValues, [a.fieldId]: match.display },
            purposeLookupPicks: {
              ...d.purposeLookupPicks,
              [a.fieldId]: {
                token: match.token,
                recordId: match.recordId,
                display: match.display,
                details: match.details,
                hasMatchCheck: match.hasMatchCheck,
              },
            },
          }))
        }
        setPreviousNotes((n) => ({ ...n, [a.fieldId]: note }))
      }
      lookupsApi
        .recheck(a.lookupSourceId, { query: a.query, context, recordId: a.recordId })
        .then((match) => settle(match?.token ? PREVIOUS_NOTES.confirmed : PREVIOUS_NOTES.notFound, match))
        .catch(() => settle(PREVIOUS_NOTES.unavailable))
    }
    return true
  }

  /** Visitor record this registration joins, as the server picks it: face match first, then the booking. */
  const comparedVisitorId = draft.recognizedVisitorId || arrivalVisitorId || ''
  /** Picked records (e.g. a student) whose earlier visitors are compared with this visitor. */
  const recordChecks = useMemo(
    () =>
      purposesWithFields.flatMap((purpose) =>
        (purpose.fields ?? []).flatMap((field) => {
          const pick = field.id ? draft.purposeLookupPicks[field.id] : undefined
          if (!field.id || field.type !== 'lookup' || !field.lookupSourceId || !pick?.recordId) return []
          return [
            {
              key: `${field.id}|${field.lookupSourceId}|${pick.recordId}|${comparedVisitorId}`,
              purposeId: purpose.id,
              sourceId: field.lookupSourceId,
              recordId: pick.recordId,
              display: pick.display,
            },
          ]
        }),
      ),
    [purposesWithFields, draft.purposeLookupPicks, comparedVisitorId],
  )
  const recordChecksKey = recordChecks.map((c) => c.key).join(',')
  const [recordVisitors, setRecordVisitors] = useState<Record<string, LookupRecordVisitorsDto>>({})
  const [confirmedNewPeople, setConfirmedNewPeople] = useState<string[]>([])
  const fetchedRecordChecks = useRef(new Set<string>())
  useEffect(() => {
    for (const check of recordChecks) {
      if (fetchedRecordChecks.current.has(check.key)) continue
      fetchedRecordChecks.current.add(check.key)
      visitorsApi
        .recordVisitors({ sourceId: check.sourceId, recordId: check.recordId, visitorId: comparedVisitorId })
        .then((result) => setRecordVisitors((r) => ({ ...r, [check.key]: result })))
        .catch(() => fetchedRecordChecks.current.delete(check.key))
    }
  }, [recordChecksKey])
  const newPersonWarnings = recordChecks.filter((c) => recordVisitors[c.key]?.isNewPerson)
  const newPersonWarningsKey = newPersonWarnings.map((c) => c.key).join(',')
  const soundedNewPeople = useRef(new Set<string>())
  useEffect(() => {
    const fresh = newPersonWarnings.filter((c) => !soundedNewPeople.current.has(c.key))
    if (fresh.length === 0) return
    fresh.forEach((c) => soundedNewPeople.current.add(c.key))
    speakAlert(newPersonAlertText(fresh[0].display))
  }, [newPersonWarningsKey])

  function patch(partial: Partial<VisitorWizardDraft>) {
    setDraft((d) => ({ ...d, ...partial }))
  }

  function linkBooking(booking: BookingMatchDto) {
    const purposeIds = booking.purposeIds.filter((id) => purposes.some((p) => p.id === id))
    const othersChecked = !!(othersPurpose && purposeIds.includes(othersPurpose.id))
    const host = employees.find((e) => e.id === booking.hostEmployeeId)
    const bookedGender = booking.gender === 1 || booking.gender === 2 ? booking.gender : ''
    setDraft((d) => ({
      ...d,
      // A confirmed returning visitor keeps the details on file; otherwise the booking is the source.
      visitorName: d.recognizedVisitorId ? d.visitorName : booking.visitorName,
      companyName: d.recognizedVisitorId ? d.companyName : booking.companyName,
      gender: d.gender === 1 || d.gender === 2 ? d.gender : bookedGender,
      telephone: d.telephone || sanitizePhoneDigits(booking.phone || ''),
      email: d.recognizedVisitorId && d.email.trim() ? d.email : (booking.email || d.email).trim(),
      hostEmployeeId: booking.hostEmployeeId,
      hostName: host?.fullName || booking.hostName,
      departmentId: host?.departmentId || booking.departmentId,
      purposeIds,
      othersChecked,
      otherPurposeText: othersChecked ? booking.purposeNotes || '' : '',
      numberOfPersons: booking.numberOfPersons || d.numberOfPersons,
      isWalkIn: false,
      expectedVisitId: booking.visitId,
    }))
    setLinkedBooking(booking)
    setArrivalVisitorId(booking.visitorId)
    setBookingMatches([])
    setError(null)
  }

  function unlinkBooking() {
    if (linkedBooking) setDismissedBookingIds((ids) => [...ids, linkedBooking.visitId])
    setLinkedBooking(null)
    setArrivalVisitorId(null)
    patch({ expectedVisitId: null, isWalkIn: true })
  }

  // A booking opened at the desk (from the list, its QR or a booking match) must be met by the person booked.
  useEffect(() => {
    setBookingFace(null)
    setFaceMismatchConfirmed(false)
    if (!draft.photoBase64 || !draft.expectedVisitId || !online) return
    let cancelled = false
    visitorsApi.checkBookingFace(draft.expectedVisitId, draft.photoBase64)
      .then((result) => {
        if (!cancelled) setBookingFace(result)
      })
      .catch(() => {
        /* the server checks again on register */
      })
    return () => {
      cancelled = true
    }
  }, [draft.photoBase64, draft.expectedVisitId, online])

  function validateAll(): string | null {
    if (!draft.photoBase64) return 'Capture the visitor face photo to continue.'
    if (bookingFace?.result === 'Mismatch' && !faceMismatchConfirmed) {
      return 'The face does not match the photo on this booking. Check the visitor’s ID and tick the confirmation to continue.'
    }
    if (faceMatch) return 'Confirm whether this is the same visitor before continuing.'
    if (employeeMatch) return 'Confirm whether this is the group employee before continuing.'
    if (!draft.visitorName.trim()) return 'Visitor name is required.'
    if (!draft.companyName.trim()) return 'Company name is required.'
    if (draft.gender !== 1 && draft.gender !== 2) return 'Please select Mr. or Ms.'
    const mobile = sanitizePhoneDigits(draft.telephone)
    if (mobile.length > 0 && mobile.length !== 10) return 'Mobile number must be exactly 10 digits.'
    if (mobile.length === 10 && !/^[6-9]/.test(mobile)) return 'Mobile number must start with 6, 7, 8, or 9.'
    const emailError = validateVisitorEmail(draft.email)
    if (emailError) return emailError
    if (!Number.isInteger(draft.numberOfPersons) || draft.numberOfPersons < 1 || draft.numberOfPersons > 99) {
      return 'Number of persons must be between 1 and 99.'
    }
    if (!draft.hostEmployeeId) return 'Select the employee to meet.'
    if (!draft.departmentId) return 'Department could not be determined for the selected employee.'
    if (!hasPurposeSelection()) return 'Select at least one purpose.'
    if (draft.othersChecked && !draft.otherPurposeText.trim()) {
      return 'Enter the specific reason when Others is selected.'
    }
    for (const purpose of purposesWithFields) {
      for (const field of purpose.fields ?? []) {
        if (field.isRequired && field.id && !draft.purposeFieldValues[field.id]?.trim()) {
          return `${field.label} is required for ${purpose.name}.`
        }
      }
    }
    const unconfirmed = newPersonWarnings.find((c) => !confirmedNewPeople.includes(c.key))
    if (unconfirmed) {
      return `This person has not come for ${unconfirmed.display} before. Check with the family, then tick the confirmation.`
    }
    const hasIdType = !!draft.idTypeName.trim()
    const hasIdNumber = !!draft.idNumber.trim()
    if (hasIdType !== hasIdNumber) {
      return 'Provide both ID type and ID number, or leave both blank.'
    }
    if (hasIdType && !STATIC_ID_TYPES.includes(draft.idTypeName as (typeof STATIC_ID_TYPES)[number])) {
      return 'Select a valid ID type.'
    }
    return null
  }

  function resetWizard() {
    clientRequestId.current = newUuid()
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    setDraft(emptyDraft())
    setError(null)
    setFaceMatch(null)
    setEmployeeMatch(null)
    setEmployeeVisitorMatch(null)
    setFaceStatus(null)
    setEmailTouched(false)
    setBookingMatches([])
    setDismissedBookingIds([])
    setLinkedBooking(null)
    setArrivalVisitorId(null)
    fetchedRecordChecks.current.clear()
    soundedNewPeople.current.clear()
    setRecordVisitors({})
    setConfirmedNewPeople([])
  }

  async function startCamera() {
    try {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      const stream = await openFaceCamera()
      streamRef.current = stream
      if (videoRef.current) {
        videoRef.current.srcObject = stream
        await videoRef.current.play()
      }
    } catch {
      // Camera failures stay silent; Capture restarts the camera.
    }
  }

  async function capturePhoto() {
    const video = videoRef.current
    if (!video || faceBusy) return
    const photoBase64 = captureFaceJpeg(video)
    if (!photoBase64) {
      void startCamera()
      return
    }

    setFaceBusy(true)
    setError(null)
    setFaceStatus(null)
    setFaceMatch(null)
    setEmployeeMatch(null)
    setEmployeeVisitorMatch(null)

    if (!online) {
      setError('Connect to the network to validate that exactly one face is in the photo.')
      setFaceBusy(false)
      return
    }

    setFaceStatus('Checking face…')
    try {
      await visitorsApi.validatePhoto(photoBase64)
    } catch (e) {
      setError(apiErrorMessage(e, 'No face detected. Please position one person in front of the camera.'))
      setFaceStatus(null)
      setFaceBusy(false)
      return
    }

    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    patch({ photoBase64 })
    setFaceStatus('Ready to capture — checking for a previous visit…')

    try {
      const [visitorResult, employeeResult] = await Promise.allSettled([
        visitorsApi.faceSearch(photoBase64),
        visitorsApi.employeeFaceSearch(photoBase64),
      ])
      const match = visitorResult.status === 'fulfilled' ? visitorResult.value : null
      const employee = employeeResult.status === 'fulfilled' ? employeeResult.value : null
      if (match?.isCurrentlyInside) {
        setError(
          `${match.visitorName} is already checked in. Check them out before registering again.`,
        )
        setFaceMatch(null)
        patch({ photoBase64: '', recognizedVisitorId: null })
        setFaceStatus(null)
        void startCamera()
      } else if (employee) {
        setEmployeeMatch(employee)
        setEmployeeVisitorMatch(match)
        setFaceStatus(null)
      } else if (match) {
        setFaceMatch(match)
        setFaceStatus(null)
      } else if (visitorResult.status === 'rejected') {
        setFaceStatus('Returning-visitor lookup unavailable — continue and fill the details manually.')
      } else {
        setFaceStatus('No previous visit found — continue as a new visitor.')
      }
    } finally {
      setFaceBusy(false)
    }
  }

  function acceptEmployeeMatch() {
    if (!employeeMatch) return
    const previous = employeeVisitorMatch
    patch({
      visitorName: employeeMatch.fullName,
      companyName: employeeMatch.companyName || employeeMatch.locationName || '',
      gender:
        employeeMatch.gender === 1 || employeeMatch.gender === 2
          ? employeeMatch.gender
          : previous?.gender === 1 || previous?.gender === 2 ? previous.gender : '',
      telephone: sanitizePhoneDigits(employeeMatch.phone || previous?.phone || ''),
      email: (employeeMatch.email || previous?.email || '').trim(),
      recognizedVisitorId: previous?.visitorId ?? null,
      groupEmployeeUserId: employeeMatch.userId ?? null,
      groupEmployeeId: employeeMatch.employeeId ?? null,
    })
    setFaceStatus(
      `Group employee ${employeeMatch.fullName}${employeeMatch.locationName ? ` from ${employeeMatch.locationName}` : ''} — official visit, no visitor emails will be sent.`,
    )
    setEmployeeMatch(null)
    setEmployeeVisitorMatch(null)
    setError(null)
  }

  function declineEmployeeMatch() {
    const previous = employeeVisitorMatch
    setEmployeeMatch(null)
    setEmployeeVisitorMatch(null)
    if (previous) setFaceMatch(previous)
    else setFaceStatus('Continue as a visitor.')
  }

  function acceptFaceMatch() {
    if (!faceMatch) return
    if (faceMatch.isCurrentlyInside) {
      setError(
        `${faceMatch.visitorName} is already checked in. Check them out before registering again.`,
      )
      setFaceMatch(null)
      patch({ photoBase64: '', recognizedVisitorId: null })
      void startCamera()
      return
    }
    patch({
      visitorName: faceMatch.visitorName,
      companyName: faceMatch.companyName,
      gender: faceMatch.gender === 1 || faceMatch.gender === 2 ? faceMatch.gender : '',
      telephone: sanitizePhoneDigits(faceMatch.phone || ''),
      email: (faceMatch.email || '').trim(),
      recognizedVisitorId: faceMatch.visitorId,
    })
    setFaceStatus(`Details loaded from ${faceMatch.visitorNumber}. Review and register.`)
    setFaceMatch(null)
    setError(null)
  }

  function retakePhoto() {
    patch({ photoBase64: '', recognizedVisitorId: null, groupEmployeeUserId: null, groupEmployeeId: null })
    setFaceMatch(null)
    setEmployeeMatch(null)
    setEmployeeVisitorMatch(null)
    setFaceStatus(null)
    setError(null)
  }

  useEffect(() => {
    if (draft.photoBase64 || loadingExpected) return
    void startCamera()
    return () => {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null
    }
  }, [draft.photoBase64, loadingExpected])

  async function submit() {
    setEmailTouched(true)
    const msg = validateAll()
    if (msg) {
      const unconfirmed = newPersonWarnings.find((c) => !confirmedNewPeople.includes(c.key))
      if (unconfirmed) speakAlert(newPersonAlertText(unconfirmed.display))
      setError(msg)
      formTopRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      return
    }
    if (!online) {
      setError('You are offline. Reconnect to submit this registration.')
      return
    }
    setSubmitting(true)
    setError(null)
    const body: RegisterVisitorRequest = {
      visitorName: draft.visitorName.trim(),
      gender: draft.gender as 1 | 2,
      companyName: draft.companyName.trim(),
      telephone: (() => {
        const mobile = sanitizePhoneDigits(draft.telephone)
        return mobile.length ? mobile : null
      })(),
      email: draft.email.trim(),
      visitDate: draft.visitDate || null,
      visitTime: draft.visitTime ? `${draft.visitTime}:00` : null,
      numberOfPersons: draft.numberOfPersons,
      departmentId: draft.departmentId,
      hostEmployeeId: draft.hostEmployeeId || null,
      hostName: draft.hostName.trim() || null,
      purposeIds: syncOthersPurposeIds(draft.purposeIds, draft.othersChecked),
      locationIds: draft.locationId ? [draft.locationId] : [],
      plantNumber: null,
      otherLocationText: null,
      purposeNotes: draft.othersChecked ? draft.otherPurposeText.trim() : null,
      purposeFields: purposesWithFields
        .flatMap((p) => p.fields ?? [])
        .filter((f) => f.id && draft.purposeFieldValues[f.id]?.trim())
        .map((f) => ({
          fieldId: f.id!,
          value: draft.purposeFieldValues[f.id!].trim(),
          lookupToken: f.type === 'lookup' ? draft.purposeLookupPicks[f.id!]?.token ?? null : null,
        })),
      notes: draft.notes.trim() || null,
      isWalkIn: draft.isWalkIn,
      expectedVisitId: draft.expectedVisitId,
      faceMismatchConfirmed: bookingFace?.result === 'Mismatch' && faceMismatchConfirmed,
      recognizedVisitorId: draft.recognizedVisitorId,
      groupEmployeeUserId: draft.groupEmployeeUserId,
      groupEmployeeId: draft.groupEmployeeId,
      skipBookingMatch: !draft.expectedVisitId && dismissedBookingIds.length > 0,
      photoBase64: draft.photoBase64 || null,
      idTypeName: draft.idTypeName.trim(),
      idNumber: draft.idNumber.trim(),
      clientRequestId: clientRequestId.current,
    }
    try {
      const result = await visitorsApi.register(body)
      const awaitingApproval = String(result.statusLabel ?? '').toLowerCase().includes('pending')

      let passCode = result.passCode || null
      let checkInPass: PassDto | null = null
      if (!awaitingApproval) {
        const [generated, checkedIn] = await Promise.allSettled([
          passApi.generate(result.visitId),
          visitorsApi.checkIn(result.visitId),
        ])
        checkInPass = checkedIn.status === 'fulfilled' ? checkedIn.value : null
        passCode =
          checkInPass?.passCode ||
          (generated.status === 'fulfilled' && generated.value?.passCode) ||
          passCode
      }

      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null

      navigate('/visitors/welcome', {
        replace: true,
        state: {
          visitorName: draft.visitorName.trim(),
          salutation: draft.gender === 1 ? 'Mr.' : draft.gender === 2 ? 'Ms.' : result.salutation || '',
          pendingApproval: awaitingApproval,
          visitNumber: result.visitNumber,
          visitId: result.visitId,
          passCode,
          meetingHallName: checkInPass?.meetingHallName,
          meetingHallIntercom: checkInPass?.meetingHallIntercom,
          meetingHallWarning: checkInPass?.meetingHallWarning,
        },
      })
    } catch (e) {
      const message = apiErrorMessage(e, 'Registration failed.')
      // The server's own face check caught a mismatch the screen had not shown yet.
      if (message.includes('does not match the photo on this booking') && bookingFace?.result !== 'Mismatch')
        setBookingFace({ result: 'Mismatch', visitorName: draft.visitorName.trim() })
      setError(message)
      formTopRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    } finally {
      setSubmitting(false)
    }
  }

  if (loadingExpected) {
    return <Spinner label="Preparing check-in…" />
  }

  return (
    <div>
      {faceBusy ? <LoadingMask label="Checking face…" /> : null}
      {submitting ? <LoadingMask label="Registering visitor…" /> : null}
      <div className="no-print" ref={formTopRef}>
        <PageHeader
          eyebrow="Registration"
          title={isArrival ? 'Expected arrival check-in' : 'New Visitor'}
          subtitle={
            isArrival
              ? 'Capture face photo first, then confirm details and register'
              : 'Capture face photo first, then complete registration on one form'
          }
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                if (isArrival) navigate('/visitors/expected')
                else resetWizard()
              }}
            >
              {isArrival ? 'Back to expected' : 'Clear form'}
            </Button>
          }
        />
      </div>

      {error ? (
        <div className="mb-4">
          <Alert tone="error">{error}</Alert>
        </div>
      ) : null}
      {!online ? (
        <div className="mb-4">
          <Alert tone="warning" persistent>Offline — submit when connected.</Alert>
        </div>
      ) : null}

      {showFaceStage ? (
        <Panel>
          <SectionTitle>Face capture</SectionTitle>
          <div className="grid gap-6 lg:grid-cols-2">
            <div>
              <FieldLabel>Face capture</FieldLabel>
              <FaceCameraPanel
                videoRef={videoRef}
                photoUrl={draft.photoBase64 || null}
                onSwitchCamera={() => {
                  toggleCameraFacing()
                  void startCamera()
                }}
              />
              <div className="mt-3 flex flex-wrap justify-center gap-2">
                {draft.photoBase64 ? (
                  <Button variant="secondary" onClick={retakePhoto} disabled={faceBusy}>
                    Retake
                  </Button>
                ) : (
                  <Button onClick={() => void capturePhoto()} disabled={faceBusy}>
                    {faceBusy ? 'Checking face…' : 'Capture & identify'}
                  </Button>
                )}
              </div>
              <p className="mt-3 text-xs text-ink-muted">
                Capture a photo first. The registration form opens after capture
                {faceMatch || employeeMatch ? ' (confirm the match below)' : ''}.
              </p>
            </div>

            <div className="space-y-3">
              {faceStatus && !faceMatch && !employeeMatch ? <Alert tone="info">{faceStatus}</Alert> : null}

              {employeeMatch ? (
                <div className="rounded-2xl border-2 border-primary-deep/30 bg-mint/40 p-4">
                  <h3 className="text-sm font-semibold text-primary-deep">Group employee recognised</h3>
                  <div className="mt-3 min-w-0 space-y-0.5">
                    <div className="truncate font-semibold text-ink">{employeeMatch.fullName}</div>
                    <div className="truncate text-sm text-ink-muted">
                      {[employeeMatch.designation, employeeMatch.departmentName].filter(Boolean).join(' · ') || employeeMatch.companyName}
                    </div>
                    <div className="text-xs text-ink-muted">
                      {employeeMatch.locationName ? `Works at ${employeeMatch.locationName}` : employeeMatch.companyName}
                      {employeeMatch.employeeCode ? ` · ${employeeMatch.employeeCode}` : ''}
                    </div>
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    <Button onClick={acceptEmployeeMatch}>Yes — official visit</Button>
                    <Button variant="secondary" onClick={declineEmployeeMatch}>
                      Not an employee
                    </Button>
                  </div>
                  <p className="mt-3 text-xs text-ink-muted">
                    Employees of the group visiting on official work are registered as usual, but no visitor
                    emails are sent to them.
                  </p>
                </div>
              ) : null}

              {faceMatch ? (
                <div className="rounded-2xl border-2 border-primary-deep/30 bg-aqua-light/40 p-4">
                  <h3 className="text-sm font-semibold text-primary-deep">Is this the same visitor?</h3>
                  <div className="mt-3 flex items-center gap-3">
                    {faceMatch.photoUrl ? (
                      <SecureImage src={faceMatch.photoUrl} alt="" className="h-20 w-20 rounded-xl object-cover" />
                    ) : null}
                    <div className="min-w-0">
                      <div className="truncate font-semibold text-ink">{faceMatch.visitorName}</div>
                      <div className="truncate text-sm text-ink-muted">{faceMatch.companyName}</div>
                      <div className="mt-1 text-xs text-ink-muted">
                        {faceMatch.visitorNumber}
                        {faceMatch.lastVisitDate ? ` · last visit ${faceMatch.lastVisitDate}` : ''}
                        {faceMatch.totalVisits > 0 ? ` · ${faceMatch.totalVisits} visit(s)` : ''}
                      </div>
                    </div>
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    <Button onClick={acceptFaceMatch}>Yes — load their details</Button>
                    <Button variant="secondary" onClick={retakePhoto}>
                      Not them — retake photo
                    </Button>
                  </div>
                  <p className="mt-3 text-xs text-ink-muted">
                    Existing visitor details were found. Load them to continue this visit, or retake the
                    photo. Registering as a different new visitor with this face is not allowed.
                  </p>
                </div>
              ) : employeeMatch ? null : (
                <p className="text-sm text-ink-muted">
                  Stand close so only one face fills the oval. Capture is rejected when no face or multiple faces are detected.
                </p>
              )}
            </div>
          </div>
        </Panel>
      ) : (
        <div className="space-y-4">
          <Panel>
            <div className="flex flex-wrap items-center gap-4">
              {draft.photoBase64 ? (
                <img
                  src={draft.photoBase64}
                  alt="Captured visitor"
                  className="h-20 w-20 rounded-xl object-cover ring-1 ring-border"
                />
              ) : null}
              <div className="min-w-0 flex-1">
                {faceStatus ? <Alert tone="success">{faceStatus}</Alert> : null}
                {draft.recognizedVisitorId && !faceStatus ? (
                  <Alert tone="success">Returning visitor confirmed — details prefilled below. Fill any missing fields (e.g. Mr./Ms.) before submitting.</Alert>
                ) : null}
                {!faceStatus && !draft.recognizedVisitorId ? (
                  <p className="text-sm text-ink-muted">Photo captured. Complete the details below.</p>
                ) : null}
              </div>
              <Button variant="secondary" onClick={retakePhoto}>
                Change photo
              </Button>
            </div>
            {bookingFace?.result === 'Match' ? (
              <div className="mt-3">
                <Alert tone="success" persistent>
                  Face matches the photo on file for {bookingFace.visitorName}.
                </Alert>
              </div>
            ) : null}
            {bookingFace?.result === 'NoReference' ? (
              <p className="mt-3 text-sm text-ink-muted">
                No photo is on file for this booking to compare with. Check the visitor’s ID if you are unsure.
              </p>
            ) : null}
            {bookingFace?.result === 'Mismatch' ? (
              <div className="mt-3 space-y-2 rounded-2xl border-2 border-danger/40 bg-red-50 p-4">
                <p className="text-sm font-semibold text-danger">
                  This face does not match the photo on file for {bookingFace.visitorName || 'the booked visitor'}.
                </p>
                <p className="text-sm text-ink">
                  Retake the photo, or check the visitor’s ID before letting them in. The check-in is recorded with
                  your name if you continue.
                </p>
                <label className="flex items-start gap-2 text-sm font-medium text-ink">
                  <input
                    type="checkbox"
                    className="mt-0.5 h-4 w-4"
                    checked={faceMismatchConfirmed}
                    onChange={(e) => setFaceMismatchConfirmed(e.target.checked)}
                  />
                  I have checked this person’s ID and they are the booked visitor.
                </label>
              </div>
            ) : null}
          </Panel>

          {linkedBooking ? (
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border-2 border-primary/40 bg-mint/30 p-4">
              <p className="text-sm text-ink">
                <span className="font-semibold">Linked to booking {linkedBooking.visitNumber}</span> — meeting{' '}
                {linkedBooking.hostName}
                {linkedBooking.expectedTime ? ` at ${linkedBooking.expectedTime.slice(0, 5)}` : ''}. Details were
                filled from the booking; review and register.
              </p>
              <Button variant="secondary" onClick={unlinkBooking}>
                Unlink booking
              </Button>
            </div>
          ) : null}

          {!linkedBooking && visibleBookingMatches.length > 0 ? (
            <div className="space-y-3 rounded-2xl border-2 border-primary-deep/30 bg-aqua-light/40 p-4">
              <h3 className="text-sm font-semibold text-primary-deep">
                {visibleBookingMatches.length === 1
                  ? 'This visitor has an appointment booked for today'
                  : 'This visitor has appointments booked for today'}
              </h3>
              {visibleBookingMatches.map((m) => (
                <div
                  key={m.visitId}
                  className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-white/80 p-3 ring-1 ring-border"
                >
                  <div className="min-w-0 text-sm">
                    <div className="font-semibold text-ink">
                      {m.visitorName}
                      {m.companyName ? <span className="font-normal text-ink-muted"> · {m.companyName}</span> : null}
                    </div>
                    <div className="text-ink-muted">
                      Meeting {m.hostName}
                      {m.departmentName ? ` (${m.departmentName})` : ''}
                      {m.expectedTime ? ` at ${m.expectedTime.slice(0, 5)}` : ''}
                      {m.purposes.length ? ` · ${m.purposes.join(', ')}` : ''}
                    </div>
                    <div className="text-xs text-ink-muted">Booking {m.visitNumber}</div>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button onClick={() => linkBooking(m)}>Use this booking</Button>
                    <Button
                      variant="secondary"
                      onClick={() => setDismissedBookingIds((ids) => [...ids, m.visitId])}
                    >
                      Not this visitor
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          ) : null}

          <Panel>
            <SectionTitle>Visitor details</SectionTitle>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="sm:col-span-2">
                <FieldLabel htmlFor="visitorName">Visitor name *</FieldLabel>
                <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                  <div className="flex shrink-0 gap-2">
                    <Button
                      type="button"
                      variant={draft.gender === 1 ? 'primary' : 'secondary'}
                      onClick={() => patch({ gender: 1 })}
                    >
                      Mr.
                    </Button>
                    <Button
                      type="button"
                      variant={draft.gender === 2 ? 'primary' : 'secondary'}
                      onClick={() => patch({ gender: 2 })}
                    >
                      Ms.
                    </Button>
                  </div>
                  <TextInput
                    id="visitorName"
                    className="min-w-0 flex-1"
                    value={draft.visitorName}
                    onChange={(e) => patch({ visitorName: e.target.value })}
                    disabled={returningVisitor}
                    readOnly={returningVisitor}
                  />
                </div>
                {draft.gender !== 1 && draft.gender !== 2 ? (
                  <p className="mt-1 text-xs font-medium text-danger">
                    {draft.recognizedVisitorId
                      ? 'Please select Mr. or Ms. (not on file for this visitor yet).'
                      : 'Please select'}
                  </p>
                ) : null}
              </div>
              <div>
                <FieldLabel htmlFor="companyName">Company *</FieldLabel>
                <TextInput
                  id="companyName"
                  value={draft.companyName}
                  onChange={(e) => patch({ companyName: e.target.value })}
                  disabled={returningVisitor}
                  readOnly={returningVisitor}
                />
              </div>
              <div>
                <FieldLabel htmlFor="telephone">Mobile</FieldLabel>
                <div className="flex">
                  <span className="inline-flex min-h-11 shrink-0 items-center rounded-l-md border border-r-0 border-gray-200 bg-gray-50 px-3 text-sm font-semibold text-gray-700">
                    +91
                  </span>
                  <TextInput
                    id="telephone"
                    className="rounded-l-none"
                    inputMode="numeric"
                    autoComplete="tel-national"
                    placeholder="10-digit number"
                    maxLength={10}
                    value={draft.telephone}
                    onChange={(e) => patch({ telephone: sanitizePhoneDigits(e.target.value) })}
                    disabled={returningVisitor}
                    readOnly={returningVisitor}
                  />
                </div>
              </div>
              <div>
                <FieldLabel htmlFor="email">Email *</FieldLabel>
                <TextInput
                  id="email"
                  type="email"
                  inputMode="email"
                  autoComplete="email"
                  required
                  maxLength={150}
                  placeholder="name@company.com"
                  value={draft.email}
                  onChange={(e) => patch({ email: e.target.value.replace(/\s/g, '') })}
                  onBlur={() => setEmailTouched(true)}
                  disabled={returningVisitor}
                  readOnly={returningVisitor}
                />
                <FieldError message={emailTouched ? validateVisitorEmail(draft.email) : null} />
              </div>
              <div>
                <FieldLabel htmlFor="visitDate">Visit date</FieldLabel>
                <TextInput
                  id="visitDate"
                  type="date"
                  value={draft.visitDate}
                  onChange={(e) => patch({ visitDate: e.target.value })}
                />
              </div>
              <div>
                <FieldLabel htmlFor="visitTime">Visit time</FieldLabel>
                <TextInput
                  id="visitTime"
                  type="time"
                  readOnly
                  value={draft.visitTime}
                  className="bg-gray-50 text-gray-700"
                  title="Set automatically to current time"
                />
              </div>
              <div>
                <FieldLabel htmlFor="numberOfPersons">Number of persons *</FieldLabel>
                <TextInput
                  id="numberOfPersons"
                  type="text"
                  inputMode="numeric"
                  autoComplete="off"
                  maxLength={2}
                  required
                  placeholder="e.g. 3"
                  value={draft.numberOfPersons > 0 ? String(draft.numberOfPersons) : ''}
                  onChange={(e) => {
                    const digits = e.target.value.replace(/\D/g, '').slice(0, 2)
                    patch({ numberOfPersons: digits ? Number.parseInt(digits, 10) : 0 })
                  }}
                />
              </div>
            </div>
          </Panel>

          <Panel>
            <SectionTitle>Person to meet</SectionTitle>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <EmployeeSearchField
                  employees={employees}
                  value={draft.hostEmployeeId}
                  onSelect={(emp) => {
                    if (!emp) {
                      patch({ hostEmployeeId: '', hostName: '', departmentId: '' })
                      return
                    }
                    patch({
                      hostEmployeeId: emp.id,
                      hostName: emp.fullName,
                      departmentId: emp.departmentId,
                    })
                  }}
                />
              </div>
              <div>
                <FieldLabel htmlFor="departmentId">Department</FieldLabel>
                <TextInput
                  id="departmentId"
                  readOnly
                  className="bg-gray-50 text-gray-700"
                  value={departmentName || (draft.hostEmployeeId ? '—' : 'Select employee first')}
                />
              </div>
              {draft.hostEmployeeId ? (
                <div className="sm:col-span-2 text-sm text-ink-muted">
                  {(() => {
                    const emp = employees.find((x) => x.id === draft.hostEmployeeId)
                    if (!emp) return null
                    return (
                      <p>
                        {emp.email ? `Email: ${emp.email}` : 'No email on file'}
                        {emp.phone ? ` · Phone: ${emp.phone}` : ''}
                        {emp.designation ? ` · ${emp.designation}` : ''}
                      </p>
                    )
                  })()}
                </div>
              ) : null}
            </div>
          </Panel>

          <Panel>
            <SectionTitle>Purpose</SectionTitle>
            <p className="mb-3 text-sm text-ink-muted">Select one or more visit purposes.</p>
            {previousAnswers.length > 0 ? (
              <div className="mb-4 rounded-xl border border-border bg-aqua-light/40 p-3">
                <p className="mb-2 text-sm font-medium text-ink">From earlier visits</p>
                <div className="grid gap-2 sm:grid-cols-2">
                  {previousAnswers.map((set, i) => (
                    <SelectChip
                      key={`${set.purposeId}-${i}`}
                      selected={appliedPrevious === i}
                      onToggle={() => applyPreviousAnswers(i)}
                    >
                      {previousAnswersLabel(set)}
                    </SelectChip>
                  ))}
                </div>
                <p className="mt-2 text-xs text-ink-muted">Tap one to fill in that purpose and its details again.</p>
              </div>
            ) : null}
            <div className="grid gap-2 sm:grid-cols-2">
              {listedPurposes.map((p) => (
                <SelectChip
                  key={p.id}
                  selected={draft.purposeIds.includes(p.id)}
                  onToggle={() => patch({ purposeIds: toggleId(draft.purposeIds, p.id) })}
                >
                  {p.name}
                </SelectChip>
              ))}
              <SelectChip
                selected={draft.othersChecked}
                onToggle={() => {
                  const nextSelected = !draft.othersChecked
                  patch({
                    othersChecked: nextSelected,
                    otherPurposeText: nextSelected ? draft.otherPurposeText : '',
                    purposeIds: syncOthersPurposeIds(draft.purposeIds, nextSelected),
                  })
                }}
              >
                Others
              </SelectChip>
            </div>
            {draft.othersChecked ? (
              <div className="mt-4">
                <FieldLabel htmlFor="otherPurposeText">Specific reason *</FieldLabel>
                <TextInput
                  id="otherPurposeText"
                  placeholder="Describe the purpose of visit"
                  value={draft.otherPurposeText}
                  onChange={(e) => patch({ otherPurposeText: e.target.value })}
                />
              </div>
            ) : null}
            {purposesWithFields.map((purpose) => (
              <div key={purpose.id} className="mt-4 rounded-xl border border-border bg-mint/40 p-3">
                <p className="mb-2 text-sm font-semibold text-ink">{purpose.name} details</p>
                <div className="grid gap-3 sm:grid-cols-2">
                  {(purpose.fields ?? []).map((field) =>
                    field.id ? (
                      <div key={field.id}>
                        <FieldLabel htmlFor={`purpose-field-${field.id}`}>
                          {field.label}
                          {field.isRequired ? ' *' : ''}
                        </FieldLabel>
                        {field.type === 'lookup' && field.lookupSourceId ? (
                          <LookupFieldInput
                            id={`purpose-field-${field.id}`}
                            sourceId={field.lookupSourceId}
                            minChars={lookupSources.find((s) => s.id === field.lookupSourceId)?.minChars ?? 3}
                            value={draft.purposeFieldValues[field.id] ?? ''}
                            pick={draft.purposeLookupPicks[field.id]}
                            telephone={sanitizePhoneDigits(draft.telephone)}
                            email={draft.email.trim()}
                            context={Object.fromEntries(
                              (purpose.fields ?? [])
                                .filter((f) => f.id && f.id !== field.id)
                                .map((f) => [f.label, (draft.purposeFieldValues[f.id!] ?? '').trim()]),
                            )}
                            note={previousNotes[field.id]}
                            onChange={(value, pick) => {
                              setDraft((d) => {
                                const picks = { ...d.purposeLookupPicks }
                                if (pick) picks[field.id!] = pick
                                else delete picks[field.id!]
                                return {
                                  ...d,
                                  purposeFieldValues: { ...d.purposeFieldValues, [field.id!]: value },
                                  purposeLookupPicks: picks,
                                }
                              })
                              setPreviousNotes((n) => {
                                if (!(field.id! in n)) return n
                                const next = { ...n }
                                delete next[field.id!]
                                return next
                              })
                            }}
                          />
                        ) : (
                          <TextInput
                            id={`purpose-field-${field.id}`}
                            maxLength={500}
                            value={draft.purposeFieldValues[field.id] ?? ''}
                            onChange={(e) =>
                              patch({ purposeFieldValues: { ...draft.purposeFieldValues, [field.id!]: e.target.value } })
                            }
                          />
                        )}
                      </div>
                    ) : null,
                  )}
                </div>
                {newPersonWarnings
                  .filter((check) => check.purposeId === purpose.id)
                  .map((check) => (
                    <div key={check.key} className="mt-3 rounded-xl border border-amber-300 bg-amber-50 p-3">
                      <p className="text-sm font-semibold text-amber-900">
                        First time this person has come for {check.display}
                      </p>
                      <p className="mt-1 text-xs text-amber-900/80">
                        Earlier visits were by the people below. Check with the family (for example, call the parent)
                        before registering.
                      </p>
                      <ul className="mt-2 grid gap-2 sm:grid-cols-2">
                        {recordVisitors[check.key].visitors.map((v) => (
                          <li key={v.visitorId} className="flex items-center gap-3 rounded-lg bg-white/80 p-2">
                            {v.photoUrl ? (
                              <SecureImage src={v.photoUrl} alt="" className="h-14 w-14 shrink-0 rounded-lg object-cover" />
                            ) : (
                              <div className="h-14 w-14 shrink-0 rounded-lg bg-border" />
                            )}
                            <div className="min-w-0 text-xs text-ink-muted">
                              <p className="truncate text-sm font-medium text-ink">{v.visitorName}</p>
                              {v.phone ? <p>{v.phone}</p> : null}
                              <p>
                                Last visit {formatDateTime(v.lastCheckInAt)}
                                {v.visits > 1 ? ` · ${v.visits} visits` : ''}
                              </p>
                            </div>
                          </li>
                        ))}
                      </ul>
                      <label className="mt-3 flex items-start gap-2 text-sm font-medium text-amber-900">
                        <input
                          type="checkbox"
                          className="mt-1 accent-primary"
                          checked={confirmedNewPeople.includes(check.key)}
                          onChange={(e) =>
                            setConfirmedNewPeople((keys) =>
                              e.target.checked ? [...keys, check.key] : keys.filter((k) => k !== check.key),
                            )
                          }
                        />
                        I have checked that this person can come for {check.display}
                      </label>
                    </div>
                  ))}
              </div>
            ))}
          </Panel>

          <Panel>
            <SectionTitle>ID</SectionTitle>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <FieldLabel htmlFor="idTypeName">ID type</FieldLabel>
                <TextSelect
                  id="idTypeName"
                  value={draft.idTypeName}
                  onChange={(e) => patch({ idTypeName: e.target.value })}
                >
                  <option value="">Select ID type</option>
                  {STATIC_ID_TYPES.map((name) => (
                    <option key={name} value={name}>
                      {name}
                    </option>
                  ))}
                </TextSelect>
              </div>
              <div>
                <FieldLabel htmlFor="idNumber">ID number</FieldLabel>
                <TextInput
                  id="idNumber"
                  value={draft.idNumber}
                  onChange={(e) => patch({ idNumber: e.target.value })}
                />
              </div>
            </div>
          </Panel>

          <Panel>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-sm text-ink-muted">Pass number is assigned automatically on submit.</p>
              <Button
                variant="primary"
                size="lg"
                loading={submitting}
                disabled={!online || faceBusy}
                onClick={() => void submit()}
              >
                {submitting ? 'Registering…' : 'Register Visitor'}
              </Button>
            </div>
          </Panel>
        </div>
      )}
    </div>
  )
}
