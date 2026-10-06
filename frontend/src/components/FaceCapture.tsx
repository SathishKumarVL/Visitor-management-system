import { useCallback, useEffect, useRef, useState } from 'react'
import { FaceCameraPanel } from './FaceCameraPanel'
import { Button } from './ui/Button'
import { Alert } from './ui/Panel'
import { captureFaceJpeg } from '../lib/cameraCapture'

type Props = {
  onCapture: (photoBase64: string) => void
  /** When set, the still is shown instead of the live camera, and the camera is released. */
  photoUrl?: string | null
  busy?: boolean
  captureLabel?: string
  cameraErrorText?: string
}

/** Front camera with the face oval and a Capture button. Releases the camera when hidden or unmounted. */
export function FaceCapture({
  onCapture,
  photoUrl = null,
  busy = false,
  captureLabel = 'Capture',
  cameraErrorText = 'Unable to access the camera. Allow camera permission and try again.',
}: Props) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const [ready, setReady] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    setReady(false)
  }, [])

  const start = useCallback(async () => {
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
      setReady(true)
    } catch {
      setError(cameraErrorText)
      setReady(false)
    }
  }, [cameraErrorText])

  useEffect(() => {
    if (photoUrl) {
      stop()
      return
    }
    void start()
    return () => stop()
  }, [photoUrl, start, stop])

  function capture() {
    const video = videoRef.current
    if (!video) return
    const jpeg = captureFaceJpeg(video)
    if (jpeg) onCapture(jpeg)
  }

  return (
    <div className="space-y-3">
      <FaceCameraPanel videoRef={videoRef} photoUrl={photoUrl} />
      {error ? (
        <div className="space-y-2">
          <Alert tone="error">{error}</Alert>
          <Button type="button" variant="secondary" className="w-full" onClick={() => void start()}>
            Start camera
          </Button>
        </div>
      ) : null}
      {!photoUrl && !error ? (
        <Button
          type="button"
          variant="primary"
          className="w-full"
          disabled={!ready || busy}
          loading={busy}
          onClick={capture}
        >
          {captureLabel}
        </Button>
      ) : null}
    </div>
  )
}
