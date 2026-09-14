import * as faceapi from '@vladmandic/face-api'
import { assetUrl } from './utils'

const MODEL_URL = '/models'
/** Euclidean distance threshold — lower is stricter. ~0.45–0.55 works well for TinyFaceDetector. */
export const FACE_MATCH_THRESHOLD = 0.52

let modelsReady: Promise<void> | null = null

export function ensureFaceModelsLoaded(): Promise<void> {
  if (!modelsReady) {
    modelsReady = (async () => {
      await Promise.all([
        faceapi.nets.tinyFaceDetector.loadFromUri(MODEL_URL),
        faceapi.nets.faceLandmark68Net.loadFromUri(MODEL_URL),
        faceapi.nets.faceRecognitionNet.loadFromUri(MODEL_URL),
      ])
    })().catch((err) => {
      modelsReady = null
      throw err
    })
  }
  return modelsReady
}

export type FaceDescriptor = Float32Array

export async function descriptorFromImageUrl(url: string): Promise<FaceDescriptor | null> {
  const img = await loadImage(assetUrl(url))
  return descriptorFromElement(img)
}

export async function descriptorFromElement(
  input: HTMLImageElement | HTMLVideoElement | HTMLCanvasElement,
): Promise<FaceDescriptor | null> {
  await ensureFaceModelsLoaded()
  const detection = await faceapi
    .detectSingleFace(input, new faceapi.TinyFaceDetectorOptions({ inputSize: 320, scoreThreshold: 0.5 }))
    .withFaceLandmarks()
    .withFaceDescriptor()
  return detection?.descriptor ?? null
}

export function matchDescriptor(
  live: FaceDescriptor,
  candidates: { id: string; label: string; descriptor: FaceDescriptor }[],
  threshold = FACE_MATCH_THRESHOLD,
): { id: string; label: string; distance: number } | null {
  let best: { id: string; label: string; distance: number } | null = null
  for (const c of candidates) {
    const distance = faceapi.euclideanDistance(live, c.descriptor)
    if (distance <= threshold && (!best || distance < best.distance)) {
      best = { id: c.id, label: c.label, distance }
    }
  }
  return best
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.crossOrigin = 'anonymous'
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error(`Could not load face photo: ${src}`))
    img.src = src
  })
}
