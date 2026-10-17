import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, passApi, visitorsApi } from '../lib/api'
import type { EmployeeDto, MasterItemDto, RegisterVisitorRequest, VisitorWizardDraft } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldError, TextInput, FieldLabel, TextSelect, SelectChip } from '../components/ui/Field'
import { newUuid, nowIsoTime, sanitizePhoneDigits, todayIsoDate, validateVisitorEmail } from '../lib/utils'
import { useOnlineStatus } from '../hooks/useOnlineStatus'
import { SecureImage } from '../components/SecureImage'
import { LoadingMask } from '../components/LoadingMask'
import { FaceCameraPanel } from '../components/FaceCameraPanel'
import { captureFaceJpeg } from '../lib/cameraCapture'
import type { BookingMatchDto, FaceSearchMatchDto, GroupEmployeeMatchDto, PassDto } from '../types/api'
import { useAuthStore } from '../store/authStore'
import { EmployeeSearchField } from '../components/EmployeeSearchField'

const LEGACY_DRAFT_KEY = 'tiaano_visitor_wizard_draft'
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
  const [bookingMatches, setBookingMatches] = useState<BookingMatchDto[]>([])
  const [dismissedBookingIds, setDismissedBookingIds] = useState<string[]>([])
  const [linkedBooking, setLinkedBooking] = useState<BookingMatchDto | null>(null)
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
        const purposeIds = p.filter((x) => visit.purposes.includes(x.name)).map((x) => x.id)
        const others = p.find((x) => x.name.trim().toLowerCase() === 'others')
        const othersChecked = !!(others && purposeIds.includes(others.id))
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
    setBookingMatches([])
    setError(null)
  }

  function unlinkBooking() {
    if (linkedBooking) setDismissedBookingIds((ids) => [...ids, linkedBooking.visitId])
    setLinkedBooking(null)
    patch({ expectedVisitId: null, isWalkIn: true })
  }

  function validateAll(): string | null {
    if (!draft.photoBase64) return 'Capture the visitor face photo to continue.'
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
  }

  async function startCamera() {
    try {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
        audio: false,
      })
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
      notes: draft.notes.trim() || null,
      isWalkIn: draft.isWalkIn,
      expectedVisitId: draft.expectedVisitId,
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
      setError(apiErrorMessage(e, 'Registration failed.'))
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
          <Alert tone="warning">Offline — submit when connected.</Alert>
        </div>
      ) : null}

      {showFaceStage ? (
        <Panel>
          <SectionTitle>Face capture</SectionTitle>
          <div className="grid gap-6 lg:grid-cols-2">
            <div>
              <FieldLabel>Face capture</FieldLabel>
              <FaceCameraPanel videoRef={videoRef} photoUrl={draft.photoBase64 || null} />
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
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={99}
                  required
                  value={draft.numberOfPersons}
                  onChange={(e) => {
                    const n = Number.parseInt(e.target.value, 10)
                    patch({ numberOfPersons: Number.isFinite(n) ? n : 1 })
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
