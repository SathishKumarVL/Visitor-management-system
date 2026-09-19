import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, passApi, visitorsApi } from '../lib/api'
import type { MasterItemDto, PassDto, RegisterVisitorRequest, VisitorWizardDraft } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldError, TextInput, FieldLabel, TextSelect, SelectChip } from '../components/ui/Field'
import { nowIsoTime, todayIsoDate, validateVisitorEmail } from '../lib/utils'
import { useOnlineStatus } from '../hooks/useOnlineStatus'
import { BrandLogo } from '../components/BrandLogo'
import { SecureImage } from '../components/SecureImage'
import type { FaceSearchMatchDto } from '../types/api'

const LEGACY_DRAFT_KEY = 'tiaano_visitor_wizard_draft'
const STATIC_ID_TYPES = ['Aadhaar', 'Pan Card', 'Passport'] as const

function sanitizeMobileDigits(value: string): string {
  return value.replace(/\D/g, '').slice(0, 10)
}

const emptyDraft = (): VisitorWizardDraft => ({
  step: 0,
  visitorName: '',
  telephone: '',
  email: '',
  companyName: '',
  visitDate: todayIsoDate(),
  visitTime: nowIsoTime(),
  numberOfPersons: 1,
  departmentId: '',
  hostName: '',
  purposeIds: [],
  othersChecked: false,
  locationIds: [],
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
  const [draft, setDraft] = useState<VisitorWizardDraft>(() => emptyDraft())
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [purposes, setPurposes] = useState<MasterItemDto[]>([])
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [completedPass, setCompletedPass] = useState<PassDto | null>(null)
  const [completedVisitId, setCompletedVisitId] = useState<string | null>(null)
  const [passPendingApproval, setPassPendingApproval] = useState(false)
  const [cameraError, setCameraError] = useState<string | null>(null)
  const [faceStatus, setFaceStatus] = useState<string | null>(null)
  const [faceBusy, setFaceBusy] = useState(false)
  const [faceMatch, setFaceMatch] = useState<FaceSearchMatchDto | null>(null)
  const [emailTouched, setEmailTouched] = useState(false)
  const [loadingExpected, setLoadingExpected] = useState(!!expectedId)
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const formTopRef = useRef<HTMLDivElement>(null)

  const othersPurpose = useMemo(
    () => purposes.find((p) => p.name.trim().toLowerCase() === 'others'),
    [purposes],
  )
  const listedPurposes = useMemo(
    () => purposes.filter((p) => p.name.trim().toLowerCase() !== 'others'),
    [purposes],
  )

  const showSuccess = !!(completedPass || passPendingApproval)
  /** Details form only after a confirmed capture (match resolved or none). */
  const showDetailsForm = !showSuccess && !!draft.photoBase64 && !faceMatch && !faceBusy
  const showFaceStage = !showSuccess && !showDetailsForm

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
    void (async () => {
      try {
        const [d, p] = await Promise.all([
          mastersApi.departments(),
          mastersApi.purposes(),
        ])
        setDepartments(d)
        setPurposes(p)

        if (expectedId) {
          setLoadingExpected(true)
          const visit = await visitorsApi.get(expectedId)
          const dept = d.find((x) => x.name === visit.departmentName)
          const purposeIds = p.filter((x) => visit.purposes.includes(x.name)).map((x) => x.id)
          const others = p.find((x) => x.name.trim().toLowerCase() === 'others')
          const othersChecked = !!(others && purposeIds.includes(others.id))
          setDraft({
            ...emptyDraft(),
            visitorName: visit.visitorName,
            companyName: visit.companyName,
            telephone: sanitizeMobileDigits(visit.phone || ''),
            email: (visit.email || '').trim(),
            visitDate: todayIsoDate(),
            visitTime: nowIsoTime(),
            departmentId: dept?.id || '',
            hostName: visit.hostName || '',
            purposeIds,
            othersChecked,
            otherPurposeText: visit.purposeNotes || '',
            isWalkIn: false,
            expectedVisitId: visit.visitId,
          })
        }
      } catch (e) {
        setError(apiErrorMessage(e))
      } finally {
        setLoadingExpected(false)
      }
    })()
    return () => {
      streamRef.current?.getTracks().forEach((t) => t.stop())
    }
  }, [expectedId])

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

  function patch(partial: Partial<VisitorWizardDraft>) {
    setDraft((d) => ({ ...d, ...partial }))
  }

  function validateAll(): string | null {
    if (!draft.photoBase64) return 'Capture the visitor face photo to continue.'
    if (faceMatch) return 'Confirm whether this is the same visitor before continuing.'
    if (!draft.visitorName.trim()) return 'Visitor name is required.'
    if (!draft.companyName.trim()) return 'Company name is required.'
    const mobile = sanitizeMobileDigits(draft.telephone)
    if (mobile.length > 0 && mobile.length !== 10) return 'Mobile number must be exactly 10 digits.'
    if (mobile.length === 10 && !/^[6-9]/.test(mobile)) return 'Mobile number must start with 6, 7, 8, or 9.'
    const emailError = validateVisitorEmail(draft.email)
    if (emailError) return emailError
    if (!Number.isInteger(draft.numberOfPersons) || draft.numberOfPersons < 1 || draft.numberOfPersons > 99) {
      return 'Number of persons must be between 1 and 99.'
    }
    if (!draft.departmentId) return 'Select a department.'
    if (!draft.hostName.trim()) return 'Enter the person to meet.'
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
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    setDraft(emptyDraft())
    setCompletedPass(null)
    setCompletedVisitId(null)
    setPassPendingApproval(false)
    setError(null)
    setFaceMatch(null)
    setFaceStatus(null)
    setEmailTouched(false)
  }

  async function startCamera() {
    setCameraError(null)
    try {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false })
      streamRef.current = stream
      if (videoRef.current) {
        videoRef.current.srcObject = stream
        await videoRef.current.play()
      }
    } catch {
      setCameraError('Unable to access camera. You can continue without a photo.')
    }
  }

  async function capturePhoto() {
    const video = videoRef.current
    if (!video || faceBusy) return
    const canvas = document.createElement('canvas')
    canvas.width = video.videoWidth || 640
    canvas.height = video.videoHeight || 480
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    ctx.drawImage(video, 0, 0)

    setFaceBusy(true)
    setError(null)
    setFaceStatus(null)
    setFaceMatch(null)

    const photoBase64 = canvas.toDataURL('image/jpeg', 0.85)

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
      const match = await visitorsApi.faceSearch(photoBase64)
      if (match?.isCurrentlyInside) {
        setError(
          `${match.visitorName} is already checked in. Check them out before registering again.`,
        )
        setFaceMatch(null)
        patch({ photoBase64: '', recognizedVisitorId: null })
        setFaceStatus(null)
        void startCamera()
      } else if (match) {
        setFaceMatch(match)
        setFaceStatus(null)
      } else {
        setFaceStatus('No previous visit found — continue as a new visitor.')
      }
    } catch {
      setFaceStatus('Returning-visitor lookup unavailable — continue and fill the details manually.')
    } finally {
      setFaceBusy(false)
    }
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
      telephone: sanitizeMobileDigits(faceMatch.phone || ''),
      email: (faceMatch.email || '').trim(),
      recognizedVisitorId: faceMatch.visitorId,
    })
    setFaceStatus(`Details loaded from ${faceMatch.visitorNumber}. Review and register.`)
    setFaceMatch(null)
    setError(null)
  }

  function rejectFaceMatch() {
    setFaceMatch(null)
    setFaceStatus(null)
  }

  function retakePhoto() {
    patch({ photoBase64: '', recognizedVisitorId: null })
    setFaceMatch(null)
    setFaceStatus(null)
    setError(null)
  }

  useEffect(() => {
    if (showSuccess || draft.photoBase64 || loadingExpected) return
    void startCamera()
    return () => {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null
    }
  }, [showSuccess, draft.photoBase64, loadingExpected])

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
      companyName: draft.companyName.trim(),
      telephone: (() => {
        const mobile = sanitizeMobileDigits(draft.telephone)
        return mobile.length ? mobile : null
      })(),
      email: draft.email.trim(),
      visitDate: draft.visitDate || null,
      visitTime: draft.visitTime ? `${draft.visitTime}:00` : null,
      numberOfPersons: draft.numberOfPersons,
      departmentId: draft.departmentId,
      hostName: draft.hostName.trim(),
      purposeIds: syncOthersPurposeIds(draft.purposeIds, draft.othersChecked),
      locationIds: [],
      plantNumber: null,
      otherLocationText: null,
      purposeNotes: draft.othersChecked ? draft.otherPurposeText.trim() : null,
      notes: draft.notes.trim() || null,
      isWalkIn: draft.isWalkIn,
      expectedVisitId: draft.expectedVisitId,
      recognizedVisitorId: draft.recognizedVisitorId,
      photoBase64: draft.photoBase64 || null,
      idTypeName: draft.idTypeName.trim(),
      idNumber: draft.idNumber.trim(),
    }
    try {
      const result = await visitorsApi.register(body)
      setCompletedVisitId(result.visitId)

      const awaitingApproval = String(result.statusLabel ?? '').toLowerCase().includes('pending')
      if (awaitingApproval) {
        setPassPendingApproval(true)
        streamRef.current?.getTracks().forEach((t) => t.stop())
        streamRef.current = null
        return
      }

      const [passSettled] = await Promise.allSettled([
        passApi.generate(result.visitId),
        visitorsApi.checkIn(result.visitId),
      ])
      if (passSettled.status === 'fulfilled') {
        setCompletedPass(passSettled.value)
        patch({ passNumber: passSettled.value.passCode || '' })
      } else {
        setCompletedPass({
          passId: result.visitId,
          visitId: result.visitId,
          visitNumber: result.visitNumber,
          visitorName: draft.visitorName.trim(),
          companyName: draft.companyName.trim(),
          hostName: draft.hostName.trim(),
          departmentName: departments.find((d) => d.id === draft.departmentId)?.name || '',
          purposes: purposes.filter((p) => draft.purposeIds.includes(p.id)).map((p) => p.name),
          locations: [],
          passCode: result.visitNumber,
          photoUrl: null,
          status: 'Issued',
        })
      }
      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null
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
              {isArrival
                ? 'Back to expected'
                : showSuccess
                  ? 'Register another'
                  : 'Clear form'}
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

      {showSuccess ? (
        <Panel>
          <div id="visitor-registration-print" className="visitor-pass-print space-y-3">
            {completedPass ? (
              <div className="no-print">
                <Alert tone="success">
                  Visitor registered and checked in. No print needed — use Reception → Check Out (face) when they leave. Print remains optional.
                </Alert>
              </div>
            ) : (
              <div className="no-print">
                <Alert tone="info">
                  Visitor registered and is pending approval. Print the pass after approval from Visitor Passes or the visitor record.
                </Alert>
              </div>
            )}

            {completedPass ? (
              <div className="mb-2 flex items-start justify-between gap-3 border-b border-gray-200 pb-2">
                <BrandLogo className="h-12 print:h-10" />
                <div className="shrink-0 text-right">
                  <div className="text-[10px] font-semibold uppercase tracking-wide text-gray-500">Visit #</div>
                  <div className="font-mono text-sm font-bold text-steel print:text-xs">{completedPass.visitNumber}</div>
                </div>
              </div>
            ) : null}

            <div className="grid gap-4 sm:grid-cols-[1fr_auto] print:grid-cols-[1fr_auto] print:gap-3">
              <div className="space-y-1 text-sm print:space-y-0.5 print:text-xs">
                <ReviewRow label="Visitor" value={draft.visitorName} />
                <ReviewRow label="Company" value={draft.companyName} />
                <ReviewRow label="Mobile" value={draft.telephone ? `+91 ${draft.telephone}` : '—'} />
                <ReviewRow label="Email" value={draft.email || '—'} />
                <ReviewRow label="Visit" value={`${draft.visitDate} ${draft.visitTime}`} />
                <ReviewRow label="Number of persons" value={String(draft.numberOfPersons)} />
                <ReviewRow
                  label="Department"
                  value={departments.find((d) => d.id === draft.departmentId)?.name || '—'}
                />
                <ReviewRow label="Person to meet" value={draft.hostName || '—'} />
                <ReviewRow
                  label="Purposes"
                  value={(() => {
                    const names = purposes.filter((p) => draft.purposeIds.includes(p.id)).map((p) => p.name)
                    if (draft.othersChecked && !names.some((n) => n.trim().toLowerCase() === 'others')) {
                      names.push('Others')
                    }
                    return names.join(', ') || '—'
                  })()}
                />
                {draft.othersChecked && draft.otherPurposeText.trim() ? (
                  <ReviewRow label="Other purpose detail" value={draft.otherPurposeText.trim()} />
                ) : null}
                {completedPass?.visitNumber ? (
                  <ReviewRow label="Visit #" value={completedPass.visitNumber} />
                ) : null}
                {(completedPass?.passCode || draft.passNumber.trim()) ? (
                  <ReviewRow
                    label="Pass number"
                    value={(completedPass?.passCode || draft.passNumber).trim()}
                  />
                ) : null}
              </div>

              <div className="flex flex-row items-start justify-end gap-3 sm:flex-col print:flex-row">
                {draft.photoBase64 ? (
                  <img
                    src={draft.photoBase64}
                    alt="Visitor"
                    className="h-28 w-28 rounded-md object-cover print:h-24 print:w-24"
                  />
                ) : (
                  <div className="flex h-28 w-28 items-center justify-center rounded-md bg-gray-100 text-xs text-gray-400 print:h-24 print:w-24">
                    No photo
                  </div>
                )}
              </div>
            </div>

            {completedPass && completedVisitId ? (
              <div className="no-print mt-4 flex flex-wrap gap-2 border-t border-border/70 pt-4">
                <Button
                  variant="amber"
                  onClick={() => {
                    const previousTitle = document.title
                    document.title = ' '
                    let done = false
                    const goNewVisitor = () => {
                      if (done) return
                      done = true
                      document.title = previousTitle
                      window.removeEventListener('afterprint', goNewVisitor)
                      resetWizard()
                    }
                    window.addEventListener('afterprint', goNewVisitor)
                    window.print()
                    window.setTimeout(goNewVisitor, 1200)
                  }}
                >
                  Print pass (optional)
                </Button>
                <Link to={`/visitors/${completedVisitId}`}>
                  <Button variant="secondary">Open visitor record</Button>
                </Link>
                {!isArrival ? (
                  <Button variant="primary" onClick={resetWizard}>
                    Register another
                  </Button>
                ) : (
                  <Button variant="primary" onClick={() => navigate('/visitors/expected')}>
                    Back to expected
                  </Button>
                )}
              </div>
            ) : passPendingApproval && completedVisitId ? (
              <div className="no-print mt-4 flex flex-wrap gap-2 border-t border-border/70 pt-4">
                <Link to={`/visitors/${completedVisitId}`}>
                  <Button variant="secondary">Open visitor record</Button>
                </Link>
                {!isArrival ? (
                  <Button variant="primary" onClick={resetWizard}>
                    Register another
                  </Button>
                ) : (
                  <Button variant="primary" onClick={() => navigate('/reception')}>
                    Back to reception
                  </Button>
                )}
              </div>
            ) : null}
          </div>
        </Panel>
      ) : showFaceStage ? (
        <Panel>
          <SectionTitle>Face capture</SectionTitle>
          <div className="grid gap-6 lg:grid-cols-2">
            <div>
              <FieldLabel>Face capture</FieldLabel>
              <div className="overflow-hidden rounded-2xl border border-border bg-gray-900">
                {draft.photoBase64 ? (
                  <img src={draft.photoBase64} alt="Captured visitor" className="aspect-video w-full object-cover" />
                ) : (
                  <video ref={videoRef} className="aspect-video w-full object-cover" muted playsInline />
                )}
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
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
              <FieldError message={cameraError} />
              <p className="mt-3 text-xs text-ink-muted">
                Capture a photo first. The registration form opens after capture
                {faceMatch ? ' (confirm the match below)' : ''}.
              </p>
            </div>

            <div className="space-y-3">
              {faceStatus && !faceMatch ? <Alert tone="info">{faceStatus}</Alert> : null}

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
                    <Button variant="secondary" onClick={rejectFaceMatch}>
                      No — new visitor
                    </Button>
                  </div>
                  <p className="mt-3 text-xs text-ink-muted">
                    Always confirm against the person in front of you before loading details.
                  </p>
                </div>
              ) : (
                <p className="text-sm text-ink-muted">
                  Position one person in front of the camera. Capture is rejected when no face or multiple faces are detected.
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
                  <Alert tone="success">Returning visitor confirmed — details prefilled below.</Alert>
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

          <Panel>
            <SectionTitle>Visitor details</SectionTitle>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="sm:col-span-2">
                <FieldLabel htmlFor="visitorName">Visitor name *</FieldLabel>
                <TextInput
                  id="visitorName"
                  value={draft.visitorName}
                  onChange={(e) => patch({ visitorName: e.target.value })}
                />
              </div>
              <div>
                <FieldLabel htmlFor="companyName">Company *</FieldLabel>
                <TextInput
                  id="companyName"
                  value={draft.companyName}
                  onChange={(e) => patch({ companyName: e.target.value })}
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
                    onChange={(e) => patch({ telephone: sanitizeMobileDigits(e.target.value) })}
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
                <FieldLabel htmlFor="departmentId">Department *</FieldLabel>
                <TextSelect
                  id="departmentId"
                  value={draft.departmentId}
                  onChange={(e) => patch({ departmentId: e.target.value })}
                >
                  <option value="">Select department</option>
                  {departments.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </TextSelect>
              </div>
              <div>
                <FieldLabel htmlFor="hostName">Person to meet *</FieldLabel>
                <TextInput
                  id="hostName"
                  placeholder="Person to meet"
                  value={draft.hostName}
                  onChange={(e) => patch({ hostName: e.target.value })}
                />
              </div>
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

function ReviewRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-[140px_1fr] gap-2 border-b border-gray-100 py-1.5 print:grid-cols-[120px_1fr] print:gap-1 print:border-gray-200 print:py-1">
      <div className="font-medium text-gray-500">{label}</div>
      <div className="text-gray-900">{value || '—'}</div>
    </div>
  )
}
