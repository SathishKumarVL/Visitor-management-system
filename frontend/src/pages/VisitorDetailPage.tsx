import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiErrorMessage, isConflictError, visitorsApi } from '../lib/api'
import type { VisitorDetailDto } from '../types/api'
import { Alert, Badge, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
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
  const [savingMom, setSavingMom] = useState(false)

  const [checkInOpen, setCheckInOpen] = useState(false)
  const [photoBase64, setPhotoBase64] = useState<string | null>(null)
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
      // Camera failures stay silent; Retry camera restarts it.
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
    stopCamera()
  }

  async function capturePhoto() {
    const video = videoRef.current
    if (!video || faceBusy) return
    const captured = captureFaceJpeg(video)
    if (!captured) {
      void startCamera()
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
          meetingHallName: pass?.meetingHallName,
          meetingHallIntercom: pass?.meetingHallIntercom,
          meetingHallWarning: pass?.meetingHallWarning,
        },
      })
    } catch (e) {
      setError(apiErrorMessage(e))
      if (isConflictError(e)) await load()
    } finally {
      setBusy(false)
    }
  }

  async function toggleMomNotRequired(notRequired: boolean) {
    if (!id || !visitor) return
    if (
      notRequired &&
      !window.confirm(`Mark minutes of meeting as not required for ${visitor.visitorName}? No MOM email will be sent.`)
    )
      return
    setSavingMom(true)
    setError(null)
    setMessage(null)
    try {
      const updated = await visitorsApi.setMomNotRequired(id, notRequired)
      setVisitor(updated)
      setMessage(
        notRequired ? 'Minutes of meeting marked as not required.' : 'Minutes of meeting are required again.',
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
  const canRecordMom = canEditMom && (isInside || isCheckedOut) && !visitor.momEmailSentAt
  const momActions = visitor.momActionItems ?? []
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
          <div className="flex flex-col items-center gap-3 sm:flex-row sm:items-start">
            <FaceCameraPanel videoRef={videoRef} photoUrl={photoBase64} alt={visitor.visitorName} />
            <div className="flex flex-wrap gap-2">
              {!photoBase64 ? (
                <>
                  <Button type="button" disabled={faceBusy} onClick={() => void capturePhoto()}>
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

        <div className="space-y-4">
          <Panel>
            <div className="mb-3 flex flex-wrap items-center gap-2">
              <h2 className="mr-1 font-semibold text-steel">Visit details</h2>
              <Badge className={statusBadgeClass(visitor.statusLabel)}>{visitor.statusLabel}</Badge>
              {visitor.isLongStay ? (
                <Badge className="bg-red-100 text-danger ring-1 ring-red-200">Long stay</Badge>
              ) : null}
              {visitor.isGroupEmployee ? (
                <Badge className="bg-violet-50 text-violet-800 ring-1 ring-violet-200" title="Official visit; no visitor emails are sent">
                  Group employee
                </Badge>
              ) : null}
              {isInside || isCheckedOut ? (
                visitor.momEmailSentAt ? (
                  <Badge className="bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200">MoM sent</Badge>
                ) : visitor.hasMeetingMinutes ? (
                  <Badge className="bg-sky-50 text-sky-800 ring-1 ring-sky-200">MoM saved, not sent</Badge>
                ) : visitor.momNotRequired ? (
                  <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">MoM not required</Badge>
                ) : (
                  <Badge className="bg-amber-100 text-amber-900 ring-1 ring-amber-200">MoM pending</Badge>
                )
              ) : null}
            </div>
            <div className="flex flex-col-reverse gap-4 sm:flex-row sm:items-start">
            <dl className="grid flex-1 gap-2 text-sm sm:grid-cols-2">
              <Item label="Visitor #" value={visitor.visitorNumber} />
              <Item label="Visit #" value={visitor.visitNumber} />
              <Item label="Pass No." value={visitor.passCode || '—'} />
              <Item label="Visit date" value={formatDate(visitor.visitDate)} />
              <Item label="Person to meet" value={visitor.hostName} />
              <Item label="Department" value={visitor.departmentName} />
              <Item label="Meeting hall" value={visitor.meetingHallName || '—'} />
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
              <Item
                label="ID"
                value={
                  visitor.idNumberMasked
                    ? [visitor.idTypeName, visitor.idNumberMasked].filter(Boolean).join(' ')
                    : '—'
                }
              />
              <Item label="Notes" value={visitor.notes || '—'} />
            </dl>
            {visitor.photoUrl ? (
              <SecureImage
                src={visitor.photoUrl}
                alt={visitor.visitorName}
                className="h-40 w-40 shrink-0 self-center rounded-xl object-cover ring-1 ring-border sm:self-start"
              />
            ) : (
              <div className="flex h-40 w-40 shrink-0 items-center justify-center self-center rounded-xl bg-gray-100 text-sm text-gray-400 sm:self-start">
                No photo
              </div>
            )}
            </div>
          </Panel>

          <Panel>
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <h2 className="font-semibold text-steel">Minutes of meeting</h2>
              <div className="flex flex-wrap gap-2">
                {canWaiveMom && !visitor.momNotRequired ? (
                  <Button size="sm" variant="secondary" disabled={savingMom} onClick={() => void toggleMomNotRequired(true)}>
                    MoM not required
                  </Button>
                ) : null}
                {canWaiveMom && visitor.momNotRequired ? (
                  <Button size="sm" variant="secondary" disabled={savingMom} onClick={() => void toggleMomNotRequired(false)}>
                    MoM required
                  </Button>
                ) : null}
                {canRecordMom ? (
                  <Link to={`/visitors/${visitor.visitId}/minutes`}>
                    <Button size="sm">{visitor.hasMeetingMinutes ? 'Edit minutes' : 'Record minutes'}</Button>
                  </Link>
                ) : null}
                {visitor.momEmailSentAt ? (
                  <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">Read-only · MOM email sent</Badge>
                ) : null}
              </div>
            </div>
            {visitor.momNotRequired && !visitor.hasMeetingMinutes ? (
              <div className="mb-3">
                <Alert tone="info">
                  Marked as not required{visitor.momNotRequiredBy ? ` by ${visitor.momNotRequiredBy}` : ''}
                  {visitor.momNotRequiredAt ? ` on ${formatDateTime(visitor.momNotRequiredAt)}` : ''}. No MOM email will be
                  sent.
                </Alert>
              </div>
            ) : null}
            {visitor.meetingMinutes ? (
              <div className="space-y-4 text-sm">
                <div className="grid gap-4 md:grid-cols-2">
                  <div>
                    <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-ink-muted">Discussion points</h3>
                    <p className="whitespace-pre-wrap text-gray-900">{visitor.meetingMinutes}</p>
                  </div>
                  <div>
                    <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-ink-muted">Decisions</h3>
                    <p className="whitespace-pre-wrap text-gray-900">{visitor.momDecisions || '—'}</p>
                  </div>
                </div>
                {momActions.length ? (
                  <div>
                    <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-ink-muted">Action items</h3>
                    <ol className="space-y-1">
                      {momActions.map((a, i) => (
                        <li key={i} className="rounded-md bg-gray-50 px-3 py-2">
                          <span className="font-medium text-gray-900">{i + 1}. {a.task}</span>
                          <span className="text-ink-muted">
                            {a.owner ? ` · ${a.owner}` : ''}
                            {a.dueDate ? ` · due ${formatDate(a.dueDate)}` : ''}
                          </span>
                        </li>
                      ))}
                    </ol>
                  </div>
                ) : null}
                <p className="text-ink-muted">
                  {visitor.momNextMeetingDate ? `Next meeting ${formatDate(visitor.momNextMeetingDate)} · ` : ''}
                  {visitor.momEmailSentAt
                    ? `MOM email sent ${formatDateTime(visitor.momEmailSentAt)}`
                    : 'MOM email not sent yet'}
                  {visitor.momRecordedBy ? ` · Recorded by ${visitor.momRecordedBy}` : ''}
                </p>
              </div>
            ) : (
              <p className="text-sm text-gray-500">
                No minutes of meeting recorded yet.
                {canRecordMom ? ' Record them after the meeting; the MOM email is sent separately from the check-out email.' : ''}
              </p>
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
