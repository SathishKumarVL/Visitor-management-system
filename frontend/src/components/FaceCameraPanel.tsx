import { useState, type RefObject } from 'react'
import { hasMultipleCameras } from '../lib/cameraCapture'

type Props = {
  videoRef?: RefObject<HTMLVideoElement | null>
  photoUrl?: string | null
  alt?: string
  /** Shown as a front / back camera button when the device has more than one camera. */
  onSwitchCamera?: () => void
}

/** Compact portrait camera / preview with a face guide so only one person fills the frame. */
export function FaceCameraPanel({ videoRef, photoUrl, alt = 'Face capture', onSwitchCamera }: Props) {
  const [canSwitch, setCanSwitch] = useState(false)

  return (
    <div className="mx-auto w-full max-w-[220px] md:max-w-[260px]">
      <div className="relative overflow-hidden rounded-2xl border border-border bg-gray-900 shadow-inner aspect-[3/4]">
        {photoUrl ? (
          <img src={photoUrl} alt={alt} className="h-full w-full object-cover object-center" />
        ) : (
          <>
            <video
              ref={videoRef}
              className="h-full w-full object-cover object-center"
              muted
              playsInline
              onPlaying={() => {
                if (onSwitchCamera) void hasMultipleCameras().then(setCanSwitch)
              }}
            />
            <div
              className="pointer-events-none absolute inset-[10%] rounded-[45%] border-2 border-white/75 shadow-[0_0_0_999px_rgba(0,0,0,0.35)]"
              aria-hidden
            />
            {onSwitchCamera && canSwitch ? (
              <button
                type="button"
                onClick={onSwitchCamera}
                className="absolute right-2 top-2 flex h-11 w-11 items-center justify-center rounded-full bg-black/55 text-white backdrop-blur hover:bg-black/70"
                aria-label="Switch front / back camera"
                title="Switch front / back camera"
              >
                <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden>
                  <path d="M4 8h3l2-3h6l2 3h3v11H4z" strokeLinejoin="round" />
                  <path d="M9.5 12.5a2.8 2.8 0 014.9-1.5M14.5 14a2.8 2.8 0 01-4.9 1.5" strokeLinecap="round" />
                  <path d="M14.6 9.6v1.6H13M9.4 16.4v-1.6H11" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              </button>
            ) : null}
          </>
        )}
      </div>
      {!photoUrl ? (
        <p className="mt-2 text-center text-xs text-ink-muted">
          Keep only one face inside the oval
        </p>
      ) : null}
    </div>
  )
}
