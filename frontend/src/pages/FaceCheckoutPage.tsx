import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiErrorMessage, visitorsApi } from '../lib/api'
import type { FaceCheckoutMatchDto, VisitorListItemDto } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'
import { SecureImage } from '../components/SecureImage'

export function FaceCheckoutPage() {
  const navigate = useNavigate()
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const [loading, setLoading] = useState(true)
  const [cameraReady, setCameraReady] = useState(false)
  const [capturing, setCapturing] = useState(false)
  const [statusText, setStatusText] = useState('Loading currently inside visitors…')
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [matched, setMatched] = useState<FaceCheckoutMatchDto | null>(null)
  const [filter, setFilter] = useState('')
  const [inside, setInside] = useState<VisitorListItemDto[]>([])

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

  const loadInside = useCallback(async () => {
    setLoading(true)
    setError(null)
    setMessage(null)
    setMatched(null)
    try {
      const list = await visitorsApi.inside()
      setInside(list)
      setStatusText(
        list.length === 0
          ? 'No visitors are currently inside.'
          : `Ready — ${list.length} visitor(s) inside. Press Capture to check out.`,
      )
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not load currently inside visitors.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void (async () => {
      await loadInside()
      await startCamera()
    })()
    return () => stopCamera()
  }, [loadInside, startCamera, stopCamera])

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

      // Detection, alignment and matching all happen on the server against the stored templates.
      const hit = await visitorsApi.faceIdentifyInside(canvas.toDataURL('image/jpeg', 0.85))
      if (!hit) {
        setError('Face did not match anyone currently inside. Try again or use manual check-out.')
        setStatusText('No match — press Capture to retry')
        return
      }

      setMatched(hit)
      setStatusText(`Matched ${hit.visitorName} — checking out…`)
      await visitorsApi.checkOut(hit.visitId)
      setMessage(`Checked out: ${hit.visitorName}. Out time recorded.`)
      setStatusText('Check-out complete — press Capture for the next visitor')
      setInside((list) => list.filter((v) => v.visitId !== hit.visitId))
    } catch (e) {
      setError(apiErrorMessage(e, 'Check-out failed after face capture.'))
      setMatched(null)
      setStatusText('Capture failed — try again')
    } finally {
      setCapturing(false)
    }
  }

  async function manualCheckOut(visitId: string, name: string) {
    setError(null)
    try {
      await visitorsApi.checkOut(visitId)
      setMessage(`Checked out: ${name}. Out time recorded.`)
      setInside((list) => list.filter((v) => v.visitId !== visitId))
      setMatched(null)
      if (!cameraReady) await startCamera()
      else setStatusText('Ready — press Capture to check out')
    } catch (e) {
      setError(apiErrorMessage(e, 'Manual check-out failed.'))
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
        subtitle="Paperless exit — capture the face to match their registration photo"
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => navigate('/reception')}>
              Back to Reception
            </Button>
            <Button
              variant="ghost"
              onClick={() => {
                void loadInside()
              }}
            >
              Refresh inside list
            </Button>
          </div>
        }
      />

      {loading ? <Spinner label="Loading currently inside visitors…" /> : null}

      {!loading ? (
        <div className="grid gap-4 lg:grid-cols-2">
          <Panel>
            <div className="overflow-hidden rounded-md border border-gray-200 bg-gray-900">
              <video ref={videoRef} className="aspect-video w-full object-cover" muted playsInline />
            </div>
            <p className="mt-3 text-sm text-gray-600">{statusText}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              {!cameraReady ? (
                <Button variant="secondary" onClick={() => void startCamera()}>Start camera</Button>
              ) : (
                <>
                  <Button
                    variant="amber"
                    disabled={capturing}
                    onClick={() => void captureAndCheckOut()}
                  >
                    {capturing ? 'Matching…' : 'Capture & check out'}
                  </Button>
                  <Button variant="secondary" disabled={capturing} onClick={stopCamera}>
                    Stop camera
                  </Button>
                </>
              )}
            </div>
            {error ? <div className="mt-3"><Alert tone="error">{error}</Alert></div> : null}
            {message ? <div className="mt-3"><Alert tone="success">{message}</Alert></div> : null}
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
              Tip: Face match uses the template enrolled at registration. Visitors registered before
              face recognition was enabled will not match until they register again.
            </p>
          </Panel>
        </div>
      ) : null}
    </div>
  )
}
