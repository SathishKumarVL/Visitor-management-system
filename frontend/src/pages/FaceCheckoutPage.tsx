import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiErrorMessage, isConflictError, mastersApi, visitorsApi } from '../lib/api'
import type { FaceCheckoutMatchDto, FeedbackQuestionDto, VisitorListItemDto } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'
import { SecureImage } from '../components/SecureImage'
import { cn } from '../lib/utils'

type Step = 'feedback' | 'camera'

function StarRating({
  value,
  onChange,
  label,
}: {
  value: number
  onChange: (n: number) => void
  label: string
}) {
  return (
    <div className="flex items-center gap-1" role="group" aria-label={label}>
      {[1, 2, 3, 4, 5].map((n) => (
        <button
          key={n}
          type="button"
          className={cn(
            'text-2xl leading-none transition',
            n <= value ? 'text-amber-500' : 'text-gray-300 hover:text-amber-300',
          )}
          aria-label={`${n} star${n === 1 ? '' : 's'}`}
          aria-pressed={n === value}
          onClick={() => onChange(n)}
        >
          ★
        </button>
      ))}
    </div>
  )
}

export function FaceCheckoutPage() {
  const navigate = useNavigate()
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)

  const [step, setStep] = useState<Step>('feedback')
  const [loading, setLoading] = useState(true)
  const [cameraReady, setCameraReady] = useState(false)
  const [capturing, setCapturing] = useState(false)
  const [statusText, setStatusText] = useState('Loading…')
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [matched, setMatched] = useState<FaceCheckoutMatchDto | null>(null)
  const [filter, setFilter] = useState('')
  const [inside, setInside] = useState<VisitorListItemDto[]>([])
  const [questions, setQuestions] = useState<FeedbackQuestionDto[]>([])
  /** Held in the browser until face capture identifies the visitor. */
  const [ratings, setRatings] = useState<Record<string, number>>({})
  const [comments, setComments] = useState('')
  const [feedbackReady, setFeedbackReady] = useState(false)

  const stopCamera = useCallback(() => {
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    setCameraReady(false)
  }, [])

  const startCamera = useCallback(async () => {
    setError(null)
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
      setCameraReady(true)
      setStatusText('Position the face in frame, then press Capture')
    } catch {
      setError('Unable to access camera. Allow camera permission or use manual check-out below.')
      setCameraReady(false)
    }
  }, [])

  const loadData = useCallback(async () => {
    setLoading(true)
    setError(null)
    setMessage(null)
    setMatched(null)
    try {
      const [list, qs] = await Promise.all([
        visitorsApi.inside(),
        mastersApi.feedbackQuestions(true),
      ])
      setInside(list)
      setQuestions(qs)
      if (qs.length === 0) {
        setFeedbackReady(true)
        setStep('camera')
        setStatusText(
          list.length === 0
            ? 'No visitors are currently inside.'
            : `Ready — ${list.length} visitor(s) inside. Press Capture to check out.`,
        )
      } else {
        setFeedbackReady(false)
        setStep('feedback')
        setStatusText('Rate the visit, then open the camera. Feedback is saved after face match.')
      }
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not load check-out data.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadData()
    return () => stopCamera()
  }, [loadData, stopCamera])

  useEffect(() => {
    if (step !== 'camera') {
      stopCamera()
      return
    }
    void startCamera()
  }, [step, startCamera, stopCamera])

  function validatePendingFeedback(): string | null {
    for (const q of questions) {
      const rating = ratings[q.id] ?? 0
      if (q.isRequired && (rating < 1 || rating > 5)) {
        return `Please rate: ${q.prompt}`
      }
    }
    return null
  }

  function buildFeedbackPayload() {
    return {
      answers: questions
        .map((q) => ({ questionId: q.id, rating: ratings[q.id] ?? 0 }))
        .filter((a) => a.rating >= 1 && a.rating <= 5),
      comments: comments.trim() || null,
    }
  }

  function continueToCamera() {
    const msg = validatePendingFeedback()
    if (msg) {
      setError(msg)
      return
    }
    setError(null)
    setMessage(null)
    setFeedbackReady(true)
    setStep('camera')
    setStatusText('Camera ready — capture the face. Ratings will be saved for the matched visitor.')
  }

  async function saveFeedbackForVisit(visitId: string) {
    if (questions.length === 0) return
    const existing = await visitorsApi.getFeedback(visitId)
    if (existing) return
    await visitorsApi.submitFeedback(visitId, buildFeedbackPayload())
  }

  async function captureAndCheckOut() {
    const video = videoRef.current
    if (!video || capturing) return
    setError(null)
    setMessage(null)
    setMatched(null)

    if (inside.length === 0) {
      setError('Nobody is currently inside. Check in a visitor first, then try again.')
      return
    }

    if (questions.length > 0 && !feedbackReady) {
      setError('Complete the star ratings first, then open the camera.')
      setStep('feedback')
      return
    }

    if (questions.length > 0) {
      const msg = validatePendingFeedback()
      if (msg) {
        setError(msg)
        setStep('feedback')
        return
      }
    }

    if (video.readyState < 2) {
      setError('Camera is not ready yet. Wait a moment and try Capture again.')
      return
    }

    setCapturing(true)
    setStatusText('Capturing and matching face…')
    try {
      const canvas = document.createElement('canvas')
      canvas.width = video.videoWidth || 640
      canvas.height = video.videoHeight || 480
      const ctx = canvas.getContext('2d')
      if (!ctx) {
        setError('Could not read the camera frame. Try again.')
        return
      }
      ctx.drawImage(video, 0, 0)

      const hit = await visitorsApi.faceIdentifyInside(canvas.toDataURL('image/jpeg', 0.85))
      if (!hit) {
        setError('Face did not match anyone currently inside. Try again or use manual check-out.')
        setStatusText('No match — press Capture to retry')
        return
      }

      setMatched(hit)
      setStatusText(`Matched ${hit.visitorName} — saving feedback and checking out…`)

      // Persist ratings only after we know who was captured.
      await saveFeedbackForVisit(hit.visitId)
      await visitorsApi.checkOut(hit.visitId)

      setMessage(`Checked out: ${hit.visitorName}. Feedback saved with their visit.`)
      setStatusText('Check-out complete')
      setInside((list) => list.filter((v) => v.visitId !== hit.visitId))
      setRatings({})
      setComments('')
      setFeedbackReady(false)
      if (questions.length > 0) {
        setStep('feedback')
      }
    } catch (e) {
      setError(apiErrorMessage(e, 'Check-out failed after face capture.'))
      setMatched(null)
      setStatusText('Capture failed — try again')
      if (isConflictError(e)) {
        try {
          setInside(await visitorsApi.inside())
        } catch {
          /* keep previous list */
        }
      }
    } finally {
      setCapturing(false)
    }
  }

  async function manualCheckOut(visitId: string, name: string) {
    setError(null)
    setMessage(null)
    try {
      if (questions.length > 0) {
        const msg = validatePendingFeedback()
        if (msg || !feedbackReady) {
          setStep('feedback')
          setError(
            msg ??
              `Rate the visit first, then press “Continue to camera” (or check out ${name} again after rating).`,
          )
          return
        }
        await saveFeedbackForVisit(visitId)
      }
      await visitorsApi.checkOut(visitId)
      setMessage(`Checked out: ${name}. Feedback saved with their visit.`)
      setInside((list) => list.filter((v) => v.visitId !== visitId))
      setMatched(null)
      setRatings({})
      setComments('')
      setFeedbackReady(false)
      if (questions.length > 0) setStep('feedback')
    } catch (e) {
      setError(apiErrorMessage(e, 'Manual check-out failed.'))
      if (isConflictError(e)) {
        try {
          setInside(await visitorsApi.inside())
        } catch {
          /* keep previous list */
        }
      }
    }
  }

  const filtered = inside.filter((v) => {
    const q = filter.trim().toLowerCase()
    if (!q) return true
    return (
      v.visitorName.toLowerCase().includes(q) ||
      v.companyName.toLowerCase().includes(q) ||
      (v.phone || '').includes(q)
    )
  })

  return (
    <div>
      <PageHeader
        title="Face Check Out"
        subtitle={
          questions.length > 0
            ? 'Rate the visit, then capture the face — feedback is saved only after a match'
            : 'Paperless exit — capture the face to match their registration photo'
        }
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => navigate('/reception')}>
              Back to Reception
            </Button>
            <Button
              variant="ghost"
              onClick={() => {
                void loadData()
              }}
            >
              Refresh inside list
            </Button>
          </div>
        }
      />

      {loading ? <Spinner label="Loading check-out…" /> : null}

      {!loading ? (
        <div className="grid gap-4 lg:grid-cols-2">
          {step === 'feedback' ? (
            <Panel>
              <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                Visitor feedback
              </h2>
              <p className="mt-1 text-sm text-ink-muted">
                Rate each question (1–5 stars). Nothing is saved yet — after face capture, ratings are
                stored with that visitor.
              </p>

              <div className="mt-4 space-y-4">
                {questions.map((q) => (
                  <div key={q.id} className="rounded-xl border border-border/70 p-3">
                    <div className="mb-2 text-sm font-medium text-ink">
                      {q.prompt}
                      {q.isRequired ? <span className="text-danger"> *</span> : null}
                    </div>
                    <StarRating
                      label={q.prompt}
                      value={ratings[q.id] ?? 0}
                      onChange={(n) => setRatings((r) => ({ ...r, [q.id]: n }))}
                    />
                  </div>
                ))}
              </div>

              <div className="mt-4">
                <FieldLabel htmlFor="fb-comments">Comments (optional)</FieldLabel>
                <TextInput
                  id="fb-comments"
                  value={comments}
                  onChange={(e) => setComments(e.target.value)}
                  placeholder="Anything else to note…"
                />
              </div>

              <div className="mt-4">
                <Button
                  variant="primary"
                  disabled={inside.length === 0}
                  onClick={continueToCamera}
                >
                  Continue to camera
                </Button>
              </div>

              {error ? (
                <div className="mt-3">
                  <Alert tone="error">{error}</Alert>
                </div>
              ) : null}
              {message ? (
                <div className="mt-3">
                  <Alert tone="success">{message}</Alert>
                </div>
              ) : null}
            </Panel>
          ) : (
            <Panel>
              {questions.length > 0 ? (
                <div className="mb-3 rounded-lg border border-primary/20 bg-aqua-light/50 px-3 py-2 text-sm">
                  Ratings ready — they will be saved for whoever the camera matches.
                  <button
                    type="button"
                    className="ml-2 text-xs font-medium text-primary underline-offset-2 hover:underline"
                    onClick={() => {
                      setStep('feedback')
                      setFeedbackReady(false)
                      setMessage(null)
                      setError(null)
                    }}
                  >
                    Edit ratings
                  </button>
                </div>
              ) : null}
              <div className="overflow-hidden rounded-md border border-gray-200 bg-gray-900">
                <video ref={videoRef} className="aspect-video w-full object-cover" muted playsInline />
              </div>
              <p className="mt-3 text-sm text-gray-600">{statusText}</p>
              <div className="mt-3 flex flex-wrap gap-2">
                {!cameraReady ? (
                  <Button variant="secondary" onClick={() => void startCamera()}>
                    Start camera
                  </Button>
                ) : (
                  <Button variant="amber" disabled={capturing} onClick={() => void captureAndCheckOut()}>
                    {capturing ? 'Matching…' : 'Capture & check out'}
                  </Button>
                )}
              </div>
              {error ? (
                <div className="mt-3">
                  <Alert tone="error">{error}</Alert>
                </div>
              ) : null}
              {message ? (
                <div className="mt-3">
                  <Alert tone="success">{message}</Alert>
                </div>
              ) : null}
              {matched ? (
                <div className="mt-3 flex items-center gap-3 rounded-md border border-gray-100 p-3">
                  <SecureImage src={matched.photoUrl || ''} alt="" className="h-16 w-16 rounded object-cover" />
                  <div>
                    <div className="font-semibold text-steel">{matched.visitorName}</div>
                    <div className="text-sm text-gray-500">{matched.companyName}</div>
                    <div className="text-xs text-gray-400">
                      {matched.visitNumber} · match {(matched.similarity * 100).toFixed(0)}%
                    </div>
                  </div>
                </div>
              ) : null}
            </Panel>
          )}

          <Panel>
            <h2 className="mb-2 text-sm font-semibold text-steel">Manual check-out (fallback)</h2>
            <FieldLabel htmlFor="face-filter">Search currently inside</FieldLabel>
            <TextInput
              id="face-filter"
              className="mb-3"
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              placeholder="Name, company, or phone"
            />
            {filtered.length === 0 ? (
              <p className="text-sm text-gray-500">No visitors currently inside.</p>
            ) : (
              <ul className="max-h-[28rem] space-y-2 overflow-auto">
                {filtered.map((v) => (
                  <li
                    key={v.visitId}
                    className="flex items-center justify-between gap-3 rounded-md border border-gray-100 px-3 py-2"
                  >
                    <div className="min-w-0">
                      <div className="truncate font-medium text-gray-900">{v.visitorName}</div>
                      <div className="truncate text-xs text-gray-500">{v.companyName}</div>
                    </div>
                    <Button size="sm" variant="danger" onClick={() => void manualCheckOut(v.visitId, v.visitorName)}>
                      Check out
                    </Button>
                  </li>
                ))}
              </ul>
            )}
            <p className="mt-4 text-xs text-gray-500">
              {questions.length > 0
                ? 'Complete star ratings first. Manual check-out then saves those ratings for the selected visitor.'
                : 'Tip: Face match uses the template enrolled at registration.'}
            </p>
          </Panel>
        </div>
      ) : null}
    </div>
  )
}
