import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiErrorMessage, isConflictError, passApi, visitorsApi } from '../lib/api'
import type { VisitorDetailDto } from '../types/api'
import { Alert, Badge, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { FieldLabel } from '../components/ui/Field'
import { formatDate, formatDateTime, formatDuration, statusBadgeClass, cn } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { hasAnyRole } from '../lib/utils'
import { SecureImage } from '../components/SecureImage'
import { FaceCameraPanel } from '../components/FaceCameraPanel'
import { captureFaceJpeg } from '../lib/cameraCapture'

export function VisitorDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const canGate = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Reception', 'Security'])
  const canEditMom = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Host'])

  const [visitor, setVisitor] = useState<VisitorDetailDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [mom, setMom] = useState('')
  const [savingMom, setSavingMom] = useState(false)

  const [checkInOpen, setCheckInOpen] = useState(false)
  const [photoBase64, setPhotoBase64] = useState<string | null>(null)
  const [cameraError, setCameraError] = useState<string | null>(null)
  const [faceBusy, setFaceBusy] = useState(false)
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const noticeRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!message && !error) return
    noticeRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }, [message, error])

  async function load() {
    if (!id) return
    setLoading(true)
    setError(null)
    try {
      const v = await visitorsApi.get(id)
      setVisitor(v)
      setMom(v.meetingMinutes || '')
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  useEffect(() => {
    return () => {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null
    }
  }, [])

  async function startCamera() {
    setCameraError(null)
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
      setCameraError('Unable to access camera. Allow camera permission and try again.')
    }
  }

  function stopCamera() {
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    if (videoRef.current) videoRef.current.srcObject = null
  }

  async function openCheckIn() {
    setError(null)
    setMessage(null)
    setPhotoBase64(null)
    if (visitor?.photoUrl) {
      await checkIn(null)
      return
    }
    setCheckInOpen(true)
    await startCamera()
  }

  function cancelCheckInCapture() {
    setCheckInOpen(false)
    setPhotoBase64(null)
    setCameraError(null)
    stopCamera()
  }

  async function capturePhoto() {
    const video = videoRef.current
    if (!video || faceBusy) return
    const captured = captureFaceJpeg(video)
    if (!captured) {
      setError('Camera is not ready yet. Wait a moment and try Capture again.')
      return
    }
    setFaceBusy(true)
    setError(null)
    try {
      await visitorsApi.validatePhoto(captured)
      setPhotoBase64(captured)
      stopCamera()
    } catch (e) {
      setError(apiErrorMessage(e, 'Face photo was not accepted.'))
      setPhotoBase64(null)
    } finally {
      setFaceBusy(false)
    }
  }

  async function checkIn(photo: string | null) {
    if (!id || !visitor) return
    setBusy(true)
    setMessage(null)
    setError(null)
    try {
      const pass = await visitorsApi.checkIn(id, photo)
      setCheckInOpen(false)
      stopCamera()
      navigate('/visitors/welcome', {
        replace: true,
        state: {
          visitorName: visitor.visitorName,
          salutation: visitor.salutation || '',
          pendingApproval: false,
          visitNumber: visitor.visitNumber,
          visitId: id,
          passCode: pass?.passCode || visitor.passCode || null,
        },
      })
    } catch (e) {
      setError(apiErrorMessage(e))
      if (isConflictError(e)) await load()
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

  async function saveMom() {
    if (!id) return
    setSavingMom(true)
    setError(null)
    setMessage(null)
    try {
      const wasPending = !visitor?.hasMeetingMinutes && !visitor?.momNotRequired
      const checkedOut = String(visitor?.statusLabel ?? '').toLowerCase().includes('checkedout')
      const updated = await visitorsApi.saveMeetingMinutes(id, mom)
      setVisitor(updated)
      setMom(updated.meetingMinutes || '')
      setMessage(
        checkedOut && wasPending
          ? 'Meeting minutes saved. The check-out email has been sent to the visitor and host.'
          : checkedOut
            ? 'Meeting minutes updated.'
            : 'Meeting minutes saved. They will be included in the check-out email.',
      )
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setSavingMom(false)
    }
  }

  async function toggleMomNotRequired(notRequired: boolean) {
    if (!id || !visitor) return
    if (
      notRequired &&
      !window.confirm(
        `Mark minutes of meeting as not required for ${visitor.visitorName}? The check-out email will be sent without minutes.`,
      )
    )
      return
    setSavingMom(true)
    setError(null)
    setMessage(null)
    try {
      const checkedOut = String(visitor.statusLabel ?? '').toLowerCase().includes('checkedout')
      const updated = await visitorsApi.setMomNotRequired(id, notRequired)
      setVisitor(updated)
      setMessage(
        !notRequired
          ? 'Minutes of meeting are required again.'
          : checkedOut
            ? 'Minutes of meeting marked as not required. The check-out email has been sent to the visitor and host.'
            : 'Minutes of meeting marked as not required. The check-out email will be sent at check-out.',
      )
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setSavingMom(false)
    }
  }

  if (loading) return <Spinner label="Loading visitor…" />
  if (error && !visitor) return <Alert tone="error">{error}</Alert>
  if (!visitor) return null

  const status = String(visitor.statusLabel).toLowerCase()
  const isInside = status.includes('inside')
  const isCheckedOut = status.includes('checkedout')
  const showMomEditor = canEditMom && (isInside || isCheckedOut)
  const checkOutMailPending = isCheckedOut && !visitor.hasMeetingMinutes && !visitor.momNotRequired
  const canWaiveMom = (canGate || canEditMom) && !visitor.hasMeetingMinutes && (isInside || isCheckedOut)
  const canCheckIn = canGate && (status.includes('approved') || status.includes('expected'))

  return (
    <div>
      {busy || savingMom || faceBusy ? <LoadingMask label="Working…" /> : null}
      <PageHeader
        title={visitor.visitorName}
        subtitle={`${visitor.visitNumber} · ${visitor.companyName}`}
        actions={
          <div className="flex flex-wrap gap-2">
            <Link to="/visitors"><Button variant="secondary">Back to list</Button></Link>
            {canCheckIn ? (
              <Button disabled={busy || checkInOpen} onClick={() => void openCheckIn()}>Check in</Button>
            ) : null}
            {canGate && isInside ? (
              <Link to="/checkout/face">
                <Button variant="secondary">Go to Check Out</Button>
              </Link>
            ) : null}
            {canGate ? (
              <Button variant="amber" disabled={busy} onClick={() => void printPass()}>Print pass</Button>
            ) : null}
          </div>
        }
      />

      <div ref={noticeRef} className="scroll-mt-24">
        {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
        {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}
      </div>
      {checkInOpen ? (
        <Panel className="mb-4">
          <h2 className="mb-2 font-semibold text-steel">Capture face to check in</h2>
          <p className="mb-3 text-sm text-ink-muted">
            This visitor has no photo on file. Capture a single face before completing check-in.
          </p>
          {cameraError ? <div className="mb-3"><Alert tone="error">{cameraError}</Alert></div> : null}
          <div className="flex flex-col items-center gap-3 sm:flex-row sm:items-start">
            <FaceCameraPanel videoRef={videoRef} photoUrl={photoBase64} alt={visitor.visitorName} />
            <div className="flex flex-wrap gap-2">
              {!photoBase64 ? (
                <>
                  <Button type="button" disabled={faceBusy || !!cameraError} onClick={() => void capturePhoto()}>
                    Capture face
                  </Button>
                  <Button type="button" variant="secondary" onClick={() => void startCamera()}>
                    Retry camera
                  </Button>
                </>
              ) : (
                <>
                  <Button
                    type="button"
                    disabled={busy}
                    onClick={() => void checkIn(photoBase64)}
                  >
                    Confirm check-in
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => {
                      setPhotoBase64(null)
                      void startCamera()
                    }}
                  >
                    Retake
                  </Button>
                </>
              )}
              <Button type="button" variant="secondary" onClick={cancelCheckInCapture}>
                Cancel
              </Button>
            </div>
          </div>
        </Panel>
      ) : null}

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
          {isInside || isCheckedOut ? (
            <div className="mt-2 text-sm">
              {visitor.hasMeetingMinutes ? (
                <Badge className="bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200">MoM saved</Badge>
              ) : visitor.momNotRequired ? (
                <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">MoM not required</Badge>
              ) : (
                <Badge className="bg-amber-100 text-amber-900 ring-1 ring-amber-200">MoM pending</Badge>
              )}
            </div>
          ) : null}
        </Panel>

        <div className="space-y-4">
          <Panel>
            <h2 className="mb-3 font-semibold text-steel">Visit details</h2>
            <dl className="grid gap-2 text-sm sm:grid-cols-2">
              <Item label="Visitor #" value={visitor.visitorNumber} />
              <Item label="Visit #" value={visitor.visitNumber} />
              <Item label="Pass No." value={visitor.passCode || '—'} />
              <Item label="Visit date" value={formatDate(visitor.visitDate)} />
              <Item label="Person to meet" value={visitor.hostName} />
              <Item label="Department" value={visitor.departmentName} />
              <Item label="Phone" value={visitor.phone || '—'} />
              <Item label="Email" value={visitor.email || '—'} />
              <Item label="Intercom" value={visitor.intercom || '—'} />
              <Item label="Check-in" value={formatDateTime(visitor.checkInAt)} />
              <Item label="Check-out" value={formatDateTime(visitor.checkOutAt)} />
              <Item label="Duration" value={formatDuration(visitor.durationMinutes)} />
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
          </Panel>

          <Panel>
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <h2 className="font-semibold text-steel">Minutes of meeting</h2>
              {canWaiveMom && !visitor.momNotRequired ? (
                <Button size="sm" variant="secondary" disabled={savingMom} onClick={() => void toggleMomNotRequired(true)}>
                  MoM not required
                </Button>
              ) : null}
              {canWaiveMom && visitor.momNotRequired && isInside ? (
                <Button size="sm" variant="secondary" disabled={savingMom} onClick={() => void toggleMomNotRequired(false)}>
                  MoM required
                </Button>
              ) : null}
            </div>
            {visitor.momNotRequired && !visitor.hasMeetingMinutes ? (
              <div className="mb-3">
                <Alert tone="info">
                  Marked as not required{visitor.momNotRequiredBy ? ` by ${visitor.momNotRequiredBy}` : ''}
                  {visitor.momNotRequiredAt ? ` on ${formatDateTime(visitor.momNotRequiredAt)}` : ''}. The check-out email
                  {isCheckedOut ? ' was sent' : ' will be sent'} without minutes.
                </Alert>
              </div>
            ) : null}
            {showMomEditor ? (
              <div className="space-y-3">
                {checkOutMailPending ? (
                  <Alert tone="warning">
                    The visitor has checked out. The check-out email will be sent to the visitor and host as soon as
                    these minutes are saved.
                  </Alert>
                ) : (
                  <p className="text-sm text-ink-muted">
                    Enter MoM after the meeting. The check-out email to the visitor and host includes this text and is
                    only sent once minutes are saved.
                  </p>
                )}
                <div>
                  <FieldLabel htmlFor="meetingMinutes">Minutes of meeting *</FieldLabel>
                  <textarea
                    id="meetingMinutes"
                    rows={6}
                    maxLength={4000}
                    className="w-full rounded-md border border-gray-200 px-3 py-2 text-sm shadow-sm focus:border-steel focus:outline-none focus:ring-1 focus:ring-steel"
                    value={mom}
                    onChange={(e) => setMom(e.target.value)}
                  />
                </div>
                <Button disabled={savingMom || !mom.trim()} onClick={() => void saveMom()}>
                  {savingMom ? 'Saving…' : 'Save meeting minutes'}
                </Button>
              </div>
            ) : visitor.meetingMinutes ? (
              <p className="whitespace-pre-wrap text-sm text-gray-900">{visitor.meetingMinutes}</p>
            ) : (
              <p className="text-sm text-gray-500">No meeting minutes recorded yet.</p>
            )}
          </Panel>

          <Panel>
            <h2 className="mb-3 font-semibold text-steel">Checkout feedback</h2>
            {visitor.feedback ? (
              <div className="space-y-3 text-sm">
                <p className="text-ink-muted">
                  Submitted {formatDateTime(visitor.feedback.submittedAt)}
                  {visitor.feedback.comments ? ` · “${visitor.feedback.comments}”` : ''}
                </p>
                <ul className="space-y-2">
                  {visitor.feedback.answers.map((a, i) => (
                    <li
                      key={`${visitor.feedback!.id}-${i}`}
                      className="flex flex-wrap items-center justify-between gap-2 rounded-md bg-gray-50 px-3 py-2"
                    >
                      <span className="font-medium text-gray-900">{a.questionText}</span>
                      <span className="inline-flex gap-0.5 text-amber-500" aria-label={`${a.rating} of 5`}>
                        {[1, 2, 3, 4, 5].map((n) => (
                          <span key={n} className={cn(n <= a.rating ? 'opacity-100' : 'opacity-25')}>
                            ★
                          </span>
                        ))}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            ) : (
              <p className="text-sm text-gray-500">No checkout feedback for this visit yet.</p>
            )}
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
