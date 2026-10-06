# Face Pipeline Review

**Date:** 2026-09-19  
**Related:** `docs/IMAGESHARP_LICENSE_USAGE_AUDIT.md` (ImageSharp removed from face decode path)

## End-to-end flow

```
Frontend camera / file picker
  → JPEG / PNG / WebP as base64 in API body
  → Visitor API (validate-photo | register | face-search | face-identify-inside)
  → Convert.FromBase64String
  → MediaStorageService.IsAllowedImage (magic bytes + ≤5MB)     // no SkiaSharp
  → InsightFaceService.CountFaces / Embed
        → RgbImage.Decode          // SkiaSharp SKBitmap.Decode → packed RGB
        → ScrfdDetector.Detect
              → LetterboxTopLeft   // managed bilinear stretch + black top-left canvas
              → ToTensor NCHW RGB  // (pixel - 127.5) / 128
              → ONNX SCRFD det_10g
        → face-count gate (see below)
        → ArcFaceEmbedder          // WarpAffine 112×112 + ONNX w600k_r50
              → ToTensor           // (pixel - 127.5) / 127.5 ; L2-normalise
  → Storage (register): original bytes to App_Data/media/tenants/...  // unchanged
  → Match: cosine similarity vs VisitorFaceDescriptors (same ModelId)
```

## EXACTLY ONE FACE REQUIRED FOR VISITOR FACE VALIDATION

| Face count | Result | Error code | Message |
|------------|--------|------------|---------|
| 0 | **REJECT** | `NO_FACE_DETECTED` | No face detected. Please position one person in front of the camera. |
| 1 | **ACCEPT** | — | Ready to capture |
| 2+ | **REJECT** | `MULTIPLE_FACES_DETECTED` | Multiple faces detected. Please ensure only one person is in the frame. |

Rules enforced by `FacePhotoRules` + `InsightFaceService.EnsureExactlyOneFace` / `CountFaces`.  
The detector does **not** pick the largest face or crop one person from a crowd.

## Preprocessing contract (SCRFD)

| Step | Behaviour |
|------|-----------|
| Decode | RGB interleaved bytes (`R,G,B`), row-major |
| Letterbox | `scale = min(targetW/srcW, targetH/srcH)`; resize to `⌊src*scale⌋`; paste top-left on black `target×target` canvas (default 640) |
| Resize filter | Bilinear (same sampler as `WarpAffine`; replaces former ImageSharp Triangle) |
| Tensor | NCHW float32, RGB planes, `(v - 127.5) / 128` |

## What changed vs what did not

| Layer | Status |
|-------|--------|
| SkiaSharp decode | **New** (replaces ImageSharp `Image.Load`) |
| Letterbox bilinear | **New implementation**, same geometry semantics |
| SCRFD / ArcFace ONNX models | **Unchanged** |
| Match threshold / face pixel minimum | **Unchanged** |
| Visitor photo disk format | **Unchanged** (original upload bytes) |

## Tests covering this pipeline

- `RgbImagePreprocessTests` — JPEG/PNG/WebP decode, letterbox geometry, multi-face rule constants
- `SingleFaceCaptureTests` — 0 / 1 / 2+ reject/accept, SCRFD composite when models present
- `ArcFacePipelineTests` / `FaceGeometryTests` — embedding/geometry when models present
- `TenantIsolationTests.Face_Search_Does_Not_Match_Across_Tenants` — cross-tenant non-match
