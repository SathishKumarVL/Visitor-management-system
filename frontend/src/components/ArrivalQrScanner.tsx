import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import jsQR from 'jsqr'
import { apiErrorMessage, visitorsApi } from '../lib/api'
import { Alert } from './ui/Panel'
import { Button } from './ui/Button'
import { Modal } from './ui/Modal'
import { FieldLabel, TextInput } from './ui/Field'

const SCAN_EVERY_MS = 250
/** The same code is not looked up again within this time, so a rejected QR held to the camera is not hammered. */
const SAME_CODE_PAUSE_MS = 4000

type Props = { open: boolean; onClose: () => void }

/**
 * Reception scans the QR from the visitor's appointment email (camera, or a handheld scanner that types the
 * code). It only finds today's booking; check-in still takes the visitor's face and matches it to the booking.
 */
export function ArrivalQrScanner({ open, onClose }: Props) {
  const navigate = useNavigate()
  const videoRef = useRef<HTMLVideoElement>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const busyRef = useRef(false)
  const lastRef = useRef<{ code: string; at: number } | null>(null)
  const [cameraError, setCameraError] = useState<string | null>(null)
  const [cameraReady, setCameraReady] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [typed, setTyped] = useState('')
  const onCloseRef = useRef(onClose)
  useEffect(() => {
    onCloseRef.current = onClose
  })

  const stopCamera = useCallback(() => {
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    setCameraReady(false)
  }, [])

  const lookUp = useCallback(
    async (code: string) => {
      const value = code.trim()
      if (!value || busyRef.current) return
      const last = lastRef.current
      if (last && last.code === value && Date.now() - last.at < SAME_CODE_PAUSE_MS) return
      lastRef.current = { code: value, at: Date.now() }
      busyRef.current = true
      setBusy(true)
      setError(null)
      try {
        const booking = await visitorsApi.findByArrivalCode(value)
        stopCamera()
        onCloseRef.current()
        navigate(`/visitors/new?expectedId=${booking.visitId}`)
      } catch (err) {
        setError(apiErrorMessage(err))
      } finally {
        busyRef.current = false
        setBusy(false)
      }
    },
    [navigate, stopCamera],
  )

  useEffect(() => {
    if (!open) return
    setError(null)
    setTyped('')
    setCameraError(null)
    lastRef.current = null
    let cancelled = false
    let timer: number | undefined

    void (async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } },
          audio: false,
        })
        if (cancelled) {
          stream.getTracks().forEach((t) => t.stop())
          return
        }
        streamRef.current = stream
        const video = videoRef.current
        if (!video) return
        video.srcObject = stream
        await video.play()
        if (cancelled) return
        setCameraReady(true)
        timer = window.setInterval(() => {
          if (busyRef.current || !video.videoWidth) return
          const scale = Math.min(1, 640 / video.videoWidth)
          const w = Math.floor(video.videoWidth * scale)
          const h = Math.floor(video.videoHeight * scale)
          const canvas = (canvasRef.current ??= document.createElement('canvas'))
          canvas.width = w
          canvas.height = h
          const ctx = canvas.getContext('2d', { willReadFrequently: true })
          if (!ctx) return
          ctx.drawImage(video, 0, 0, w, h)
          const found = jsQR(ctx.getImageData(0, 0, w, h).data, w, h, { inversionAttempts: 'dontInvert' })
          if (found?.data) void lookUp(found.data)
        }, SCAN_EVERY_MS)
      } catch {
        if (!cancelled)
          setCameraError('The camera could not be started. Allow camera access, or use a handheld scanner below.')
      }
    })()

    return () => {
      cancelled = true
      if (timer) window.clearInterval(timer)
      stopCamera()
    }
  }, [open, lookUp, stopCamera])

  function submitTyped(e: FormEvent) {
    e.preventDefault()
    lastRef.current = null
    void lookUp(typed)
  }

  return (
    <Modal
      open={open}
      title="Scan appointment QR"
      subtitle="Hold the QR from the visitor’s appointment email in front of the camera."
      onClose={() => {
        stopCamera()
        onClose()
      }}
    >
      <div className="space-y-4">
        {error ? <Alert tone="error" persistent>{error}</Alert> : null}
        {cameraError ? (
          <Alert tone="warning" persistent>{cameraError}</Alert>
        ) : (
          <div className="relative mx-auto aspect-video w-full max-w-md overflow-hidden rounded-2xl bg-black">
            <video ref={videoRef} className="h-full w-full object-cover" muted playsInline />
            <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
              <div className="h-2/3 aspect-square rounded-2xl border-4 border-white/80 shadow-[0_0_0_9999px_rgba(0,0,0,0.35)]" />
            </div>
            {!cameraReady ? (
              <div className="absolute inset-0 flex items-center justify-center text-sm text-white/80">Starting camera…</div>
            ) : null}
            {busy ? (
              <div className="absolute inset-x-0 bottom-0 bg-black/60 py-2 text-center text-sm text-white">Finding booking…</div>
            ) : null}
          </div>
        )}
        <form onSubmit={submitTyped} className="space-y-2">
          <FieldLabel htmlFor="arrival-code">Handheld scanner or code</FieldLabel>
          <div className="flex gap-2">
            <TextInput
              id="arrival-code"
              autoComplete="off"
              autoFocus
              placeholder="Scan with a handheld scanner, or type the code"
              value={typed}
              disabled={busy}
              onChange={(e) => setTyped(e.target.value)}
            />
            <Button type="submit" disabled={busy || !typed.trim()}>
              Find
            </Button>
          </div>
        </form>
        <p className="text-xs text-ink-muted">
          The QR only finds today’s booking. The visitor’s face is still captured at check-in and compared with the
          booking.
        </p>
      </div>
    </Modal>
  )
}
