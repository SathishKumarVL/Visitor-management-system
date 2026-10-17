import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiErrorMessage, isConflictError, mastersApi, visitorsApi } from '../lib/api'
import type { FaceCheckoutMatchDto, FeedbackQuestionDto, VisitorListItemDto } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'
import { SecureImage } from '../components/SecureImage'
import { FaceCameraPanel } from '../components/FaceCameraPanel'
import { captureFaceJpeg } from '../lib/cameraCapture'
import { cn } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

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
  const userLocationId = useAuthStore((s) => s.user?.locationId ?? null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  /** Only the latest camera start may update state; an earlier one interrupted by a restart fails harmlessly. */
  const cameraAttemptRef = useRef(0)

  const [step, setStep] = useState<Step>('feedback')
  const [loading, setLoading] = useState(true)
  const [cameraReady, setCameraReady] = useState(false)
  const [capturing, setCapturing] = useState(false)
  const [statusText, setStatusText] = useState('Loading…')
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [matched, setMatched] = useState<FaceCheckoutMatchDto | null>(null)
  const [inside, setInside] = useState<VisitorListItemDto[]>([])
  const [questions, setQuestions] = useState<FeedbackQuestionDto[]>([])
  /** Held in the browser until face capture identifies the visitor. */
  const [ratings, setRatings] = useState<Record<string, number>>({})
  const [comments, setComments] = useState('')
  const [feedbackReady, setFeedbackReady] = useState(false)

  const stopCamera = useCallback(() => {
    cameraAttemptRef.current++
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    setCameraReady(false)
  }, [])

  const startCamera = useCallback(async () => {
    const attempt = ++cameraAttemptRef.current
    setError(null)
    try {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
        audio: false,
      })
      if (attempt !== cameraAttemptRef.current) {
        stream.getTracks().forEach((t) => t.stop())
        return
      }
      streamRef.current = stream
      if (videoRef.current) {
        videoRef.current.srcObject = stream
        await videoRef.current.play()
      }
      if (attempt !== cameraAttemptRef.current) return
      setCameraReady(true)
      setStatusText('Position the face in frame, then press Capture')
    } catch {
      if (attempt !== cameraAttemptRef.current) return
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
        mastersApi.feedbackQuestions(true, userLocationId || undefined),
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
  }, [userLocationId])

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
      void startCamera()
      return
    }

    setCapturing(true)
    setStatusText('Capturing and matching face…')
    try {
      const photoBase64 = captureFaceJpeg(video)
      if (!photoBase64) {
        void startCamera()
        return
      }

      const hit = await visitorsApi.faceIdentifyInside(photoBase64)
      if (!hit) {
        setError('Face did not match anyone currently inside. Try Capture again.')
        setStatusText('No match — press Capture to retry')
        return
      }

      setMatched(hit)
      setStatusText(`Matched ${hit.visitorName} — saving feedback and checking out…`)

      // Load questions for the matched visit location (may differ from desk user location).
      const visitQs = await mastersApi.feedbackQuestions(true, hit.locationId || userLocationId || undefined)
      setQuestions(visitQs)
      for (const q of visitQs) {
        const rating = ratings[q.id] ?? 0
        if (q.isRequired && (rating < 1 || rating > 5)) {
          setFeedbackReady(false)
          setStep('feedback')
          setError(`Please rate: ${q.prompt}`)
          setStatusText('Complete feedback for this visitor location, then capture again.')
          return
        }
      }

      // Persist ratings only after we know who was captured.
      if (visitQs.length > 0) {
        await visitorsApi.submitFeedback(hit.visitId, {
          answers: visitQs
            .map((q) => ({ questionId: q.id, rating: ratings[q.id] ?? 0 }))
            .filter((a) => a.rating >= 1),
          comments: comments.trim() || null,
        })
      }
      await visitorsApi.checkOut(hit.visitId)

      stopCamera()
      navigate('/visitors/thank-you', {
        replace: true,
        state: {
          visitorName: hit.visitorName,
          salutation: hit.salutation || '',
          visitNumber: hit.visitNumber,
        },
      })
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

  return (
    <div>
      {capturing ? <LoadingMask label="Matching face and checking out…" /> : null}
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
              Refresh
            </Button>
          </div>
        }
      />

      {loading ? <Spinner label="Loading check-out…" /> : null}

      {!loading ? (
        <div className="mx-auto max-w-2xl">
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
              <FaceCameraPanel videoRef={videoRef} />
              <p className="mt-3 text-center text-sm text-gray-600">{statusText}</p>
              <div className="mt-3 flex flex-wrap justify-center gap-2">
                {!cameraReady ? (
                  <Button variant="secondary" onClick={() => void startCamera()}>
                    Start camera
                  </Button>
                ) : (
                  <Button variant="amber" disabled={capturing} onClick={() => void captureAndCheckOut()}>
                    {capturing ? 'Matching…' : 'Capture & check out'}
                  </Button>
                )}
                {questions.length > 0 ? (
                  <Button
                    variant="secondary"
                    onClick={() => {
                      setStep('feedback')
                      setFeedbackReady(false)
                      setMessage(null)
                      setError(null)
                    }}
                  >
                    Edit ratings
                  </Button>
                ) : null}
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
        </div>
      ) : null}
    </div>
  )
}
