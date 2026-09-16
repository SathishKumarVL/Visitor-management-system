import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, passApi, visitorsApi } from '../lib/api'
import type { MasterItemDto, PassDto, RegisterVisitorRequest, VisitorWizardDraft } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldError, TextInput, FieldLabel, TextSelect, SelectChip } from '../components/ui/Field'
import { nowIsoTime, todayIsoDate, validateVisitorEmail } from '../lib/utils'
import { useOnlineStatus } from '../hooks/useOnlineStatus'
import { BrandLogo } from '../components/BrandLogo'
import { VisitorLocationStep, validateVisitorLocationStep } from '../components/visitors/VisitorLocationStep'
import { SecureImage } from '../components/SecureImage'
import { descriptorFromElement, ensureFaceModelsLoaded } from '../lib/faceMatch'
import type { FaceSearchMatchDto } from '../types/api'

const LEGACY_DRAFT_KEY = 'tiaano_visitor_wizard_draft'
const STEPS = ['Face', 'Visitor', 'Host', 'Purpose', 'Location', 'ID', 'Review'] as const

const STEP_FACE = 0
const STEP_VISITOR = 1
const STEP_HOST = 2
const STEP_PURPOSE = 3
const STEP_LOCATION = 4
const STEP_ID = 5
const STEP_REVIEW = 6

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
  idTypeId: '',
  idNumber: '',
  isWalkIn: true,
  expectedVisitId: null,
  faceDescriptor: null,
  recognizedVisitorId: null,
})

function toggleId(list: string[], id: string): string[] {
  return list.includes(id) ? list.filter((x) => x !== id) : [...list, id]
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
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [idTypes, setIdTypes] = useState<MasterItemDto[]>([])
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

  const othersPurpose = useMemo(
    () => purposes.find((p) => p.name.trim().toLowerCase() === 'others'),
    [purposes],
  )
  const listedPurposes = useMemo(
    () => purposes.filter((p) => p.name.trim().toLowerCase() !== 'others'),
    [purposes],
  )
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
    if (draft.step !== STEP_VISITOR) return
    setDraft((d) => ({ ...d, visitDate: todayIsoDate(), visitTime: nowIsoTime() }))
  }, [draft.step])

  useEffect(() => {
    void (async () => {
      try {
        const [d, p, locs, i] = await Promise.all([
          mastersApi.departments(),
          mastersApi.purposes(),
          mastersApi.locations(),
          mastersApi.idTypes(),
        ])
        setDepartments(d)
        setPurposes(p)
        setLocations(locs)
        setIdTypes(i)

        if (expectedId) {
          setLoadingExpected(true)
          const visit = await visitorsApi.get(expectedId)
          const dept = d.find((x) => x.name === visit.departmentName)
          const purposeIds = p.filter((x) => visit.purposes.includes(x.name)).map((x) => x.id)
          const others = p.find((x) => x.name.trim().toLowerCase() === 'others')
          const othersChecked = !!(others && purposeIds.includes(others.id))
          setDraft({
            ...emptyDraft(),
            step: 0,
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

  function validateStep(step: number): string | null {
    if (step === STEP_FACE) {
      if (isArrival && !draft.photoBase64) return 'Capture the visitor face photo to continue.'
      if (faceMatch) return 'Confirm whether this is the same visitor before continuing.'
    }
    if (step === STEP_VISITOR) {
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
    }
    if (step === STEP_HOST) {
      if (!draft.departmentId) return 'Select a department.'
      if (!draft.hostName.trim()) return 'Enter the host name.'
    }
    if (step === STEP_PURPOSE) {
      if (!hasPurposeSelection()) return 'Select at least one purpose.'
      if (draft.othersChecked && !draft.otherPurposeText.trim()) {
        return 'Enter the specific reason when Others is selected.'
      }
    }
    if (step === STEP_LOCATION) {
      return validateVisitorLocationStep(draft, locations)
    }
    return null
  }

  function next() {
    const msg = validateStep(draft.step)
    if (msg) {
      setError(msg)
      return
    }
    setError(null)
    if (draft.step === STEP_VISITOR) setEmailTouched(true)
    patch({ step: Math.min(STEPS.length - 1, draft.step + 1) })
  }

  function back() {
    setError(null)
    patch({ step: Math.max(0, draft.step - 1) })
  }

  function resetWizard() {
    setDraft(emptyDraft())
    setCompletedPass(null)
    setCompletedVisitId(null)
    setPassPendingApproval(false)
    setError(null)
  }

  function goToStep(step: number) {
    if (completedPass) return
    // Allow jumping to any earlier step, or any step when correcting an error
    if (step === draft.step) return
    if (step > draft.step) {
      const msg = validateStep(draft.step)
      if (msg) {
        setError(msg)
        return
      }
    }
    setError(null)
    patch({ step })
  }

  function stepForError(message: string): number {
    const m = message.toLowerCase()
    if (m.includes('telephone') || m.includes('phone') || m.includes('email') || m.includes('visitor name') || m.includes('company')) return STEP_VISITOR
    if (m.includes('department') || m.includes('host')) return STEP_HOST
    if (m.includes('purpose')) return STEP_PURPOSE
    if (m.includes('location') || m.includes('plant')) return STEP_LOCATION
    if (m.includes('photo')) return STEP_FACE
    if (m.includes('identity') || m.includes('id ')) return STEP_ID
    return draft.step
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

  /// Captures the frame, then tries to recognise a returning visitor from the face template.
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
    setFaceStatus('Reading face…')
    let descriptor: number[] | null = null
    try {
      const raw = await descriptorFromElement(video)
      descriptor = raw ? Array.from(raw) : null
    } catch {
      descriptor = null
    }

    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    patch({ photoBase64: canvas.toDataURL('image/jpeg', 0.85), faceDescriptor: descriptor })

    if (!descriptor) {
      setFaceStatus('No face detected in the photo — continue and fill the details manually, or retake.')
      setFaceBusy(false)
      return
    }

    if (!online) {
      setFaceStatus('Offline — face saved with the photo, but returning-visitor lookup was skipped.')
      setFaceBusy(false)
      return
    }

    setFaceStatus('Checking for a previous visit…')
    try {
      const match = await visitorsApi.faceSearch(descriptor)
      if (match) {
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

  /// Operator confirmed the match: prefill identity fields only. The visit itself is always new.
  function acceptFaceMatch() {
    if (!faceMatch) return
    patch({
      visitorName: faceMatch.visitorName,
      companyName: faceMatch.companyName,
      telephone: sanitizeMobileDigits(faceMatch.phone || ''),
      email: (faceMatch.email || '').trim(),
      recognizedVisitorId: faceMatch.visitorId,
      step: STEP_VISITOR,
    })
    setFaceStatus(`Details loaded from ${faceMatch.visitorNumber}. Check them, then continue.`)
    setFaceMatch(null)
    setError(null)
  }

  function rejectFaceMatch() {
    setFaceMatch(null)
    setFaceStatus('Continuing as a new visitor.')
  }

  function retakePhoto() {
    patch({ photoBase64: '', faceDescriptor: null, recognizedVisitorId: null })
    setFaceMatch(null)
    setFaceStatus(null)
  }

  useEffect(() => {
    if (draft.step !== STEP_FACE || draft.photoBase64) return
    void ensureFaceModelsLoaded().catch(() => {
      setFaceStatus('Face recognition models could not be loaded — photo capture still works.')
    })
    void startCamera()
    return () => {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null
    }
  }, [draft.step, draft.photoBase64])

  async function submit() {
    const msg =
      validateStep(STEP_FACE) ||
      validateStep(STEP_VISITOR) ||
      validateStep(STEP_HOST) ||
      validateStep(STEP_PURPOSE) ||
      validateStep(STEP_LOCATION)
    if (msg) {
      setError(msg)
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
      locationIds: draft.locationIds,
      plantNumber: draft.plantNumber.trim() || null,
      otherLocationText: draft.otherLocationText.trim() || null,
      purposeNotes: draft.othersChecked ? draft.otherPurposeText.trim() : null,
      notes: draft.notes.trim() || null,
      isWalkIn: draft.isWalkIn,
      expectedVisitId: draft.expectedVisitId,
      photoBase64: draft.photoBase64 || null,
      idTypeId: draft.idTypeId || null,
      idNumber: draft.idNumber || null,
      faceDescriptor: draft.faceDescriptor,
    }
    try {
      const result = await visitorsApi.register(body)

      // Best-effort follow-ups with a short timeout so Submit never hangs.
      const followUps = Promise.allSettled([
        passApi.generate(result.visitId),
        visitorsApi.checkIn(result.visitId),
      ])
      await Promise.race([
        followUps,
        new Promise<void>((resolve) => window.setTimeout(resolve, 8000)),
      ])

      setSubmitting(false)
      navigate('/reception', {
        replace: true,
        state: { registered: true, visitId: result.visitId, visitorName: draft.visitorName.trim() },
      })
      return
    } catch (e) {
      const message = apiErrorMessage(e, 'Registration failed.')
      const target = stepForError(message)
      setError(message)
      if (target !== draft.step) patch({ step: target })
    } finally {
      setSubmitting(false)
    }
  }

  const progress = ((draft.step + 1) / STEPS.length) * 100

  if (loadingExpected) {
    return <Spinner label="Preparing check-in wizard…" />
  }

  return (
    <div>
      <div className="no-print">
      <PageHeader
        eyebrow="Registration"
        title={isArrival ? 'Expected arrival check-in' : 'New Visitor'}
        subtitle={isArrival ? 'Confirm details, capture face photo, then submit' : 'Guided registration for reception tablets'}
        actions={
          <Button variant="secondary" onClick={() => {
            if (isArrival) navigate('/visitors/expected')
            else resetWizard()
          }}>
            {isArrival ? 'Back to expected' : (completedPass || passPendingApproval ? 'Register another' : 'Clear form')}
          </Button>
        }
      />

      <Panel className="mb-4">
        <div className="mb-2 flex items-center justify-between text-sm">
          <span className="font-medium text-ink">
            Step {draft.step + 1} of {STEPS.length}: {STEPS[draft.step]}
          </span>
          <span className="text-ink-muted">{Math.round(progress)}%</span>
        </div>
        <div className="h-2 overflow-hidden rounded-full bg-mint">
          <div className="h-full rounded-full bg-brand-gradient transition-all duration-200" style={{ width: `${progress}%` }} />
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          {STEPS.map((label, i) => {
            const isCurrent = i === draft.step
            const isDone = i < draft.step
            return (
              <button
                key={label}
                type="button"
                onClick={() => goToStep(i)}
                className={`min-h-10 rounded-xl px-3 py-1.5 text-xs font-semibold transition duration-200 ${
                  isCurrent
                    ? 'bg-brand-gradient text-white shadow-md'
                    : isDone
                      ? 'bg-aqua-light text-primary-deep hover:brightness-95'
                      : 'bg-gray-100 text-ink-muted hover:bg-gray-200'
                }`}
              >
                {i + 1}. {label}
              </button>
            )
          })}
        </div>
        <p className="mt-2 text-xs text-ink-muted">Tap a step above to go back and edit.</p>
      </Panel>
      </div>

      {error ? (
        <div className="mb-4 space-y-2">
          <Alert tone="error">{error}</Alert>
          {(error.toLowerCase().includes('telephone') || error.toLowerCase().includes('phone')) && (
            <Button variant="secondary" size="sm" onClick={() => goToStep(STEP_VISITOR)}>
              Edit telephone on Profile step
            </Button>
          )}
          {error.toLowerCase().includes('email') && (
            <Button variant="secondary" size="sm" onClick={() => goToStep(STEP_VISITOR)}>
              Edit email on Profile step
            </Button>
          )}
        </div>
      ) : null}
      {!online ? <div className="mb-4"><Alert tone="warning">Offline — submit when connected.</Alert></div> : null}

      <Panel>
        {draft.step === STEP_FACE && (
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
                  <Button variant="secondary" onClick={retakePhoto} disabled={faceBusy}>Retake</Button>
                ) : (
                  <Button onClick={() => void capturePhoto()} disabled={faceBusy}>
                    {faceBusy ? 'Checking…' : 'Capture & identify'}
                  </Button>
                )}
              </div>
              <FieldError message={cameraError} />
              <p className="mt-3 text-xs text-ink-muted">
                The photo and a face template are stored to speed up future visits. Tell the visitor before capturing.
              </p>
            </div>

            <div className="space-y-3">
              {faceStatus ? <Alert tone="info">{faceStatus}</Alert> : null}

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
                    <Button variant="secondary" onClick={rejectFaceMatch}>No — new visitor</Button>
                  </div>
                  <p className="mt-3 text-xs text-ink-muted">
                    Always confirm against the person in front of you before loading details.
                  </p>
                </div>
              ) : null}

              {!faceMatch && !faceStatus ? (
                <p className="text-sm text-ink-muted">
                  Capture the visitor's face first. If they have visited before, their details are filled in
                  automatically after you confirm the match.
                </p>
              ) : null}

              {draft.recognizedVisitorId ? (
                <Alert tone="success">Returning visitor confirmed — details prefilled on the next step.</Alert>
              ) : null}
            </div>
          </div>
        )}

        {draft.step === STEP_VISITOR && (
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <FieldLabel htmlFor="visitorName">Visitor name *</FieldLabel>
              <TextInput id="visitorName" value={draft.visitorName} onChange={(e) => patch({ visitorName: e.target.value })} />
            </div>
            <div>
              <FieldLabel htmlFor="companyName">Company *</FieldLabel>
              <TextInput id="companyName" value={draft.companyName} onChange={(e) => patch({ companyName: e.target.value })} />
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
              <TextInput id="visitDate" type="date" value={draft.visitDate} onChange={(e) => patch({ visitDate: e.target.value })} />
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
                  const raw = e.target.value
                  const n = Number.parseInt(raw, 10)
                  patch({ numberOfPersons: Number.isFinite(n) ? n : 1 })
                }}
              />
            </div>
          </div>
        )}

        {draft.step === STEP_HOST && (
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
                  <option key={d.id} value={d.id}>{d.name}</option>
                ))}
              </TextSelect>
            </div>
            <div>
              <FieldLabel htmlFor="hostName">Host *</FieldLabel>
              <TextInput
                id="hostName"
                placeholder="Person to meet"
                value={draft.hostName}
                onChange={(e) => patch({ hostName: e.target.value })}
              />
            </div>
          </div>
        )}

        {draft.step === STEP_PURPOSE && (
          <div className="space-y-4">
            <p className="text-sm text-ink-muted">Select one or more visit purposes.</p>
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
              <div>
                <FieldLabel htmlFor="otherPurposeText">Specific reason *</FieldLabel>
                <TextInput
                  id="otherPurposeText"
                  placeholder="Describe the purpose of visit"
                  value={draft.otherPurposeText}
                  onChange={(e) => patch({ otherPurposeText: e.target.value })}
                />
              </div>
            ) : null}
          </div>
        )}

        {draft.step === STEP_LOCATION && (
          <VisitorLocationStep draft={draft} locations={locations} onPatch={patch} />
        )}

        {draft.step === STEP_ID && (
          <div className="grid gap-6 lg:grid-cols-2">
            <div>
              <FieldLabel>Captured photo</FieldLabel>
              <div className="overflow-hidden rounded-2xl border border-border bg-gray-900">
                {draft.photoBase64 ? (
                  <img src={draft.photoBase64} alt="Captured visitor" className="aspect-video w-full object-cover" />
                ) : (
                  <div className="flex aspect-video w-full items-center justify-center text-sm text-white/70">
                    No photo captured
                  </div>
                )}
              </div>
              <div className="mt-3">
                <Button variant="secondary" onClick={() => goToStep(STEP_FACE)}>
                  {draft.photoBase64 ? 'Retake on Face step' : 'Capture on Face step'}
                </Button>
              </div>
            </div>
            <div className="space-y-4">
              <div>
                <FieldLabel htmlFor="idTypeId">ID type</FieldLabel>
                <TextSelect id="idTypeId" value={draft.idTypeId} onChange={(e) => patch({ idTypeId: e.target.value })}>
                  <option value="">Optional</option>
                  {idTypes.map((t) => (
                    <option key={t.id} value={t.id}>{t.name}</option>
                  ))}
                </TextSelect>
              </div>
              <div>
                <FieldLabel htmlFor="idNumber">ID number</FieldLabel>
                <TextInput id="idNumber" value={draft.idNumber} onChange={(e) => patch({ idNumber: e.target.value })} />
              </div>
            </div>
          </div>
        )}

        {draft.step === STEP_REVIEW && (
          <div id="visitor-registration-print" className="visitor-pass-print space-y-3">
            {completedPass ? (
              <div className="no-print">
                <Alert tone="success">
                  Visitor registered and checked in. No print needed — use Reception → Check Out (face) when they leave. Print remains optional.
                </Alert>
              </div>
            ) : passPendingApproval ? (
              <div className="no-print">
                <Alert tone="info">
                  Visitor registered and is pending approval. Print the pass after approval from Visitor Passes or the visitor record.
                </Alert>
              </div>
            ) : null}

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
                <ReviewRow
                  label="Mobile"
                  value={draft.telephone ? `+91 ${draft.telephone}` : '—'}
                />
                <ReviewRow label="Email" value={draft.email || '—'} />
                <ReviewRow label="Visit" value={`${draft.visitDate} ${draft.visitTime}`} />
                <ReviewRow label="Number of persons" value={String(draft.numberOfPersons)} />
                <ReviewRow label="Department" value={departments.find((d) => d.id === draft.departmentId)?.name || '—'} />
                <ReviewRow label="Host" value={draft.hostName || '—'} />
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
                <ReviewRow
                  label="Location"
                  value={locations.filter((l) => draft.locationIds.includes(l.id)).map((l) => l.name).join(', ') || '—'}
                />
                {draft.plantNumber.trim() ? <ReviewRow label="Plant number" value={draft.plantNumber.trim()} /> : null}
                {draft.otherLocationText.trim() ? (
                  <ReviewRow label="Other location" value={draft.otherLocationText.trim()} />
                ) : null}
                {completedPass?.visitNumber ? (
                  <ReviewRow label="Visit #" value={completedPass.visitNumber} />
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
                {completedPass?.visitNumber ? (
                  <div className="flex flex-col items-center gap-1 rounded-md bg-gray-50 px-3 py-2">
                    <div className="text-[10px] font-semibold uppercase tracking-wide text-gray-500">Visit number</div>
                    <div className="max-w-[140px] break-all text-center font-mono text-xs font-bold text-gray-800">
                      {completedPass.visitNumber}
                    </div>
                  </div>
                ) : null}
              </div>
            </div>

            {completedPass && completedVisitId ? (
              <div className="no-print flex flex-wrap gap-2">
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
              </div>
            ) : null}
          </div>
        )}

        <div className="no-print mt-6 flex flex-wrap justify-between gap-2 border-t border-border/70 pt-4">
          {!completedPass && !passPendingApproval ? (
            <Button variant="secondary" disabled={draft.step === STEP_FACE} onClick={back}>
              Back
            </Button>
          ) : (
            <span />
          )}
          {draft.step === STEP_REVIEW && !completedPass && !passPendingApproval ? (
            <Button variant="primary" size="lg" loading={submitting} disabled={!online} onClick={() => void submit()}>
              {submitting ? 'Registering…' : 'Register Visitor'}
            </Button>
          ) : draft.step < STEPS.length - 1 ? (
            <Button onClick={next}>Continue</Button>
          ) : null}
        </div>
      </Panel>
    </div>
  )
}

function ReviewRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-[120px_1fr] gap-2 border-b border-gray-100 py-1.5 print:grid-cols-[100px_1fr] print:gap-1 print:border-gray-200 print:py-1">
      <div className="font-medium text-gray-500">{label}</div>
      <div className="text-gray-900">{value || '—'}</div>
    </div>
  )
}
