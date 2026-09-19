# Single-face visitor photo capture

**Status:** Implemented — READY FOR REVIEW  
**Date:** 2026-09-18  
**Business rule:** A visitor registration photo must contain **exactly one** detectable face.

---

## 1. Business rule

| Detected faces | Result | Message |
|----------------|--------|---------|
| 0 | **REJECT** | `No face detected. Please position one person in front of the camera.` |
| 1 | **ACCEPT** | Continue normal capture / registration flow |
| 2+ | **REJECT** | `Multiple faces detected. Please ensure only one person is in the frame.` |

Critical: a multi-face image must **never** be saved as the visitor photo. The system does **not** auto-pick the largest face for storage.

This is **face detection / count validation**, not identity matching. Optional returning-visitor recognition remains separate and experimental.

---

## 2. Existing technology

| Layer | Technology |
|-------|------------|
| Backend detector | **SCRFD** (`det_10g.onnx`) via ONNX Runtime — already used by InsightFace / ArcFace pipeline |
| Backend embedding | ArcFace (`w600k_r50.onnx`) — used for optional revisit match only |
| Frontend | No client-side detector; camera capture calls the API |

---

## 3. Where detection occurs

1. **Capture UX** — `POST /api/visitors/validate-photo` after the frame is taken, **before** the photo is accepted into the wizard draft.  
2. **Persistence gate** — `VisitorService.SavePhotoAsync` calls `IFaceEmbeddingService.EnsureExactlyOneFace` **before** `IMediaStorageService.SaveVisitorPhotoAsync`.  
3. **Recognition path** — `Embed()` also rejects 0 / 2+ faces (no silent “largest face” selection).

---

## 4. Frontend validation

File: `frontend/src/pages/NewVisitorWizardPage.tsx`

- Capture → `visitorsApi.validatePhoto(photoBase64)`  
- On failure: show API message, **do not** set `photoBase64`, keep camera running  
- On success: accept photo, then optional `faceSearch`  
- Offline: capture blocked until the network can validate face count  

---

## 5. Backend validation

| API | Behavior |
|-----|----------|
| `POST /api/visitors/validate-photo` | Count faces; 400 + error code if not exactly one; **no storage** |
| `POST /api/visitors` with `photoBase64` | Same gate inside `SavePhotoAsync` before disk write |
| `POST /api/visitors/face-search` | Requires a single face to embed |

Error codes (in `ApiResponse.errors` when applicable):

- `NO_FACE_DETECTED`  
- `MULTIPLE_FACES_DETECTED`  
- `FACE_DETECTOR_UNAVAILABLE`  

HTTP status: **400** (existing validation convention). No stack traces, paths, or model internals.

---

## 6. Storage order

```
decode bytes → EnsureExactlyOneFace → SaveVisitorPhotoAsync → DB VisitorPhoto row
```

Rejected images are not written under `App_Data/media/tenants/{tenantId}/visitors/`.

---

## 7. Photo entry points reviewed

| Entry point | Covered? |
|-------------|----------|
| New visitor wizard camera | Yes — validate-photo + SavePhotoAsync |
| Registration API `photoBase64` | Yes — SavePhotoAsync |
| Expected visitor create | No photo field |
| Face checkout / face-search | Detection for recognition; does not store a new visitor photo |
| Direct media API upload | No public upload bypass for visitor photos |
| Admin edit photo replacement | No separate replace endpoint found; only register path |

---

## 8. Tests

`backend.Tests/SingleFaceCaptureTests.cs` (+ SCRFD fixtures when models present):

- 0 / 1 / 2 / 3+ face validation  
- Multi-face registration rejected; photo row count unchanged  
- Bypass of validate-photo via direct register still rejected  
- Real SCRFD composite of `face-a.jpg` + `face-b.jpg` ≥ 2 faces  
- Existing FaceRevisit / ArcFace suites remain green  

Synthetic stub fixtures use JPEG dimensions (no real biometrics). StyleGAN2 portraits remain the only model-quality assets.

---

## 9. Detector limitations

- SCRFD may miss heavily occluded / extreme profile / very dark faces → treated as **0 faces** (reject).  
- A face on a poster/screen in frame may count as an extra face → **reject** (correct for the rule).  
- Tiny background faces may still score above threshold → **reject** (safer than accepting).  
- When ONNX detector weights are missing, photo save **fails closed** (`FACE_DETECTOR_UNAVAILABLE`). Registration without a photo still works when photos are optional.  
- Live preview face-count overlays are not implemented (capture-time validation only) to avoid continuous ONNX load on tablets.

---

## 10. Platforms

Validated via automated tests against SQL Server + stub/real SCRFD. Manual camera checks (one person / two people / empty frame) should be run on the reception tablet after deploy.
