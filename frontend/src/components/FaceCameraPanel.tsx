import type { RefObject } from 'react'

type Props = {
  videoRef?: RefObject<HTMLVideoElement | null>
  photoUrl?: string | null
  alt?: string
}

/** Compact portrait camera / preview with a face guide so only one person fills the frame. */
export function FaceCameraPanel({ videoRef, photoUrl, alt = 'Face capture' }: Props) {
  return (
    <div className="mx-auto w-full max-w-[220px]">
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
            />
            <div
              className="pointer-events-none absolute inset-[10%] rounded-[45%] border-2 border-white/75 shadow-[0_0_0_999px_rgba(0,0,0,0.35)]"
              aria-hidden
            />
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
