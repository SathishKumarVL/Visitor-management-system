/** Centered portrait region of the live video used for face capture (excludes nearby people). */
export const FACE_CAPTURE = {
  /** Fraction of video width to keep (centered). */
  widthRatio: 0.42,
  /** Fraction of video height to keep (centered). */
  heightRatio: 0.72,
} as const

/**
 * Draw only the centered face frame from a video element into a JPEG data URL.
 * Full-frame capture pulls in people standing beside the visitor.
 */
export function captureFaceJpeg(video: HTMLVideoElement, quality = 0.85): string | null {
  const vw = video.videoWidth || 0
  const vh = video.videoHeight || 0
  if (vw < 2 || vh < 2) return null

  const cw = Math.max(64, Math.floor(vw * FACE_CAPTURE.widthRatio))
  const ch = Math.max(64, Math.floor(vh * FACE_CAPTURE.heightRatio))
  const sx = Math.floor((vw - cw) / 2)
  const sy = Math.floor((vh - ch) / 2)

  const canvas = document.createElement('canvas')
  canvas.width = cw
  canvas.height = ch
  const ctx = canvas.getContext('2d')
  if (!ctx) return null
  ctx.drawImage(video, sx, sy, cw, ch, 0, 0, cw, ch)
  return canvas.toDataURL('image/jpeg', quality)
}
