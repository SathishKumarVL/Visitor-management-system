import { useCallback, useEffect, useRef, useState } from 'react'
import { FaceCameraPanel } from './FaceCameraPanel'
import { Button } from './ui/Button'
import { captureFaceJpeg, openFaceCamera, toggleCameraFacing } from '../lib/cameraCapture'

type Props = {
  onCapture: (photoBase64: string) => void
  /** When set, the still is shown instead of the live camera, and the camera is released. */
  photoUrl?: string | null
  busy?: boolean
  captureLabel?: string
}

/** Front camera with the face oval and a Capture button. Releases the camera when hidden or unmounted. */
export function FaceCapture({
  onCapture,
  photoUrl = null,
  busy = false,
  captureLabel = 'Capture',
}: Props) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  /** Only the latest start may update state; an earlier one interrupted by a restart fails harmlessly. */
  const attemptRef = useRef(0)
  const [ready, setReady] = useState(false)
  const [failed, setFailed] = useState(false)

  const stop = useCallback(() => {
    attemptRef.current++
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    setReady(false)
  }, [])

  const start = useCallback(async () => {
    const attempt = ++attemptRef.current
    setFailed(false)
    try {
      streamRef.current?.getTracks().forEach((t) => t.stop())
      const stream = await openFaceCamera()
      if (attempt !== attemptRef.current) {
        stream.getTracks().forEach((t) => t.stop())
        return
      }
      streamRef.current = stream
      if (videoRef.current) {
        videoRef.current.srcObject = stream
        await videoRef.current.play()
      }
      if (attempt !== attemptRef.current) return
      setReady(true)
    } catch {
      if (attempt !== attemptRef.current) return
      setFailed(true)
      setReady(false)
    }
  }, [])

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
      <FaceCameraPanel
        videoRef={videoRef}
        photoUrl={photoUrl}
        onSwitchCamera={() => {
          toggleCameraFacing()
          void start()
        }}
      />
      {!photoUrl && failed ? (
        <Button type="button" variant="secondary" className="w-full" onClick={() => void start()}>
          Start camera
        </Button>
      ) : null}
      {!photoUrl && !failed ? (
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
