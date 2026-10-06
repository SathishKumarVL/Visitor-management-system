# ImageSharp License & Usage Audit

**Audit date:** 2026-09-19  
**Scope:** Read-only investigation of `SixLabors.ImageSharp` 4.1.2 usage and the Release publish license failure.  
**Constraint:** No application code, package, license, migration, commit, push, or deploy changes were made for this audit.

---

## 1. Executive Summary

`SixLabors.ImageSharp` **4.1.2** is a **direct** NuGet dependency of the API project. It was introduced in commit `7871d4f` solely to support server-side InsightFace/ArcFace face recognition.

**Production source usage is narrow and real:** only `backend/Services/Face/RgbImage.cs` calls ImageSharp APIs. Those calls decode uploaded JPEG/PNG/WebP bytes into RGB pixel buffers and perform letterbox resizing used by SCRFD detection. ONNX Runtime (`Microsoft.ML.OnnxRuntime`) performs detection and embedding; ImageSharp does **not** run the neural models.

**Visitor photo persistence does not use ImageSharp.** `MediaStorageService` validates magic bytes and writes the original bytes unchanged. ImageSharp is involved only when the face pipeline decodes the same bytes for count/embed/match.

**Release `dotnet publish` fails** because ImageSharp 4.x injects an MSBuild target `SixLabors_ValidateLicense` that **hard-fails** when no `SixLaborsLicenseKey`, `SixLaborsLicenseFile`, or `sixlabors.lic` is present. Debug builds continue on error; Release does not. The repository currently contains **no** Six Labors license configuration.

This audit does **not** conclude that a commercial license must be purchased. Eligibility under the Six Labors Split License (including possible Apache 2.0 paths) and the correct way to obtain/configure a build license key are separate business/legal decisions.

---

## 2. Current Package

| Item | Finding |
|------|---------|
| Project file | `backend/Tiaano.Vms.Api.csproj` |
| Package | `SixLabors.ImageSharp` |
| Version | **4.1.2** (exact `PackageReference`) |
| Related Six Labors packages in csproj | **None** (no ImageSharp.Drawing, Fonts, etc.) |
| Central package management | **None** — no `Directory.Packages.props` |
| Directory.Build.props | **Not present** |
| packages.lock.json | **Not present** |
| nuget.config / NuGet.Config | **Not present** in repo |
| Local license file `sixlabors.lic` | **Not found** in repository |
| MSBuild properties `SixLaborsLicenseKey` / `SixLaborsLicenseFile` | **Not configured** in project files |
| Package metadata (local NuGet cache) | `C:\Users\sathi\.nuget\packages\sixlabors.imagesharp\4.1.2\` — `LICENSE` = “Six Labors Split License”; nuspec `requireLicenseAcceptance=true` |
| Build injection | Package ships `build/SixLabors.ImageSharp.targets` with `ValidateLicenseTask` before `CoreCompile` |

**Why it was added (Git):** introduced together with `Microsoft.ML.OnnxRuntime` in `7871d4f` (“Move face recognition server-side with InsightFace ArcFace”) to decode/resize images for the C# InsightFace pipeline after removing browser `face-api.js`.

---

## 3. Direct vs Transitive Dependency

| Question | Answer | Evidence |
|----------|--------|----------|
| Direct PackageReference? | **Yes** | `backend/Tiaano.Vms.Api.csproj` line 26: `<PackageReference Include="SixLabors.ImageSharp" Version="4.1.2" />` |
| Transitive-only? | **No** | Direct reference; application code imports `SixLabors.ImageSharp*` |
| Tests project direct reference? | **No** | `backend.Tests/Tiaano.Vms.Api.Tests.csproj` has no ImageSharp PackageReference; tests get it via `ProjectReference` to the API |
| Used by QuestPDF / ClosedXML / pass PDF? | **No ImageSharp usage found** | Pass/report PDFs use QuestPDF; Excel uses ClosedXML; logo embedding uses QuestPDF `Image(...)`, not Six Labors |
| Intentional 4.x choice? | **Yes, from introduction** | Git blame: line added in `7871d4f` as `4.1.2`; no prior ImageSharp version existed in this repo |

---

## 4. Actual Source-Code Usage

### Production

| File | Class/Method | Usage | Production/Test | Required? |
|------|--------------|-------|-----------------|-----------|
| `backend/Services/Face/RgbImage.cs` ~34–37 | `RgbImage.Decode` | `Image.Load<Rgb24>(encoded)` — decode uploaded image bytes to RGB buffer | Production (runtime, on face API / photo gate / enrol / match) | **A** for face decode path; entry point for all face flows |
| `backend/Services/Face/RgbImage.cs` ~48–78 | `LetterboxTopLeft` | `ToImageSharp()` + `Mutate(Resize… Triangle)` then copy pixels — SCRFD 640×640 letterbox preprocess | Production (runtime, every SCRFD detect) | **A** for current detector preprocess |
| `backend/Services/Face/RgbImage.cs` ~158–195 | `ToImageSharp` / `FromImageSharp` | Private helpers wrapping ImageSharp pixel rows | Production (runtime) | **A** (supports Decode / Letterbox) |

**Not ImageSharp (related but separate):**

| File | Role |
|------|------|
| `backend/Services/Face/FaceEmbeddingService.cs` (`InsightFaceService`) | Calls `RgbImage.Decode`; ONNX SCRFD/ArcFace — **no** direct ImageSharp imports |
| `backend/Services/Face/ScrfdDetector.cs` | Uses `RgbImage.LetterboxTopLeft` / `PadCentered` — ImageSharp only indirectly via letterbox |
| `backend/Services/Face/ArcFaceEmbedder.cs` | `WarpAffine` on `RgbImage` (custom bilinear) — **no** ImageSharp |
| `backend/Services/MediaStorageService.cs` | Magic-byte validate + `File.WriteAllBytesAsync` — **no** ImageSharp |
| `backend/Services/VisitorService.cs` | Orchestrates photo save / face embed / validate — **no** direct ImageSharp imports |

### Tests (ImageSharp used directly)

| File | Class/Method | Usage | Production/Test | Required? |
|------|--------------|-------|-----------------|-----------|
| `backend.Tests/StubFaceEmbeddingService.cs` ~36–37 | `CountFaces` | `Image.Load` to read width for stub face-count convention | Test | **C** |
| `backend.Tests/StubFaceEmbeddingService.cs` ~96–111 | `TestPhotos` | Create solid JPEG fixtures via `Image<Rgb24>` + `JpegEncoder` | Test | **C** |
| `backend.Tests/SingleFaceCaptureTests.cs` ~278–291 | `CompositeSideBySide` | Load two JPEGs, `Mutate(DrawImage)`, save JPEG — two-face fixture | Test | **C** |
| `backend.Tests/ArcFacePipelineTests.cs` ~264–269 | `Recompress` | `Image.Load` + `JpegEncoder` quality recompress for similarity test | Test | **C** |

### Search coverage (no production hits)

Repo-wide search for ImageSharp / SixLabors / encoders / `Image<Rgba32>` / Fonts found **no** other application usages. No `SixLabors.Fonts`. No ImageSharp in pass generation, reports compression, thumbnails, or document processing.

### Dead / unused?

Production ImageSharp usage is **live**, not dead: every face count/embed/match that reaches `InsightFaceService` calls `RgbImage.Decode`, and every SCRFD pass calls `LetterboxTopLeft`.

### Startup vs runtime

ImageSharp is **not** used at application startup. Face models load lazily on first use; ImageSharp runs when those methods decode/resize images.

### Classification key (section 4 column)

- **A** = required for current face recognition / face-gated photo flows when models are available  
- **B** = optional only if product accepts disabling face recognition entirely (config/`IsAvailable` degradation) — storage still works, but face gate/search/checkout-by-face do not  
- **C** = tests only  
- **D** = not observed  
- **E** = not observed (package is direct and used)

---

## 5. Visitor Photo Flow

```
Camera / file (frontend)
  → base64 in API request body
  → VisitorService (register / validate-photo / face-search / face-identify-inside)
  → Convert.FromBase64String
  → MediaStorageService.IsAllowedImage (JPEG/PNG/WebP magic bytes, ≤5MB)     ← NO ImageSharp
  → [face gate] IFaceEmbeddingService.CountFaces / EnsureExactlyOneFace
        → InsightFaceService → RgbImage.Decode → ImageSharp Image.Load             ← ImageSharp
        → ScrfdDetector → LetterboxTopLeft → ImageSharp Resize                      ← ImageSharp
        → ONNX SCRFD (Microsoft.ML.OnnxRuntime)                                     ← NOT ImageSharp
  → MediaStorageService.SaveVisitorPhotoAsync → write original bytes to
        App_Data/media/tenants/{tenant}/visitors/{filename}                         ← NO ImageSharp
  → [enrol] Embed → Decode → Detect → ArcFace Embedder (WarpAffine + ONNX)        ← Decode uses ImageSharp
  → MediaController serves private files via auth                                   ← NO ImageSharp
```

### Does ImageSharp perform these on visitor photos?

| Operation | ImageSharp? | Actual mechanism |
|-----------|-------------|------------------|
| Resizing (for storage/display) | **No** | Original bytes stored |
| Compression | **No** | As uploaded |
| Format conversion | **No** | Extension chosen from magic bytes; bytes unchanged |
| Face detection | **Indirect preprocess only** | ImageSharp decode + letterbox; SCRFD ONNX detects |
| Metadata stripping | **No** | Not implemented |
| Validation (type/size) | **No** | `IsAllowedImage` magic bytes + size |
| Exactly-one-face validation | **Decode required** | `CountFaces` / `EnsureExactlyOneFace` via ImageSharp decode |
| Thumbnail generation | **No** | None found |
| Letterbox resize for detector | **Yes** | `LetterboxTopLeft` only (in-memory, not persisted) |

---

## 6. Face Recognition / InsightFace Relationship

| Question | Finding |
|----------|---------|
| What performs detection/recognition? | **SCRFD** (`det_10g.onnx`) and **ArcFace** (`w600k_r50.onnx`) via **Microsoft.ML.OnnxRuntime** (`InsightFaceService`, `ScrfdDetector`, `ArcFaceEmbedder`) |
| Where does image decoding happen? | `RgbImage.Decode` → **`Image.Load<Rgb24>` (ImageSharp)** |
| Is ImageSharp required before InsightFace/ArcFace? | **Yes, in the current design** — ONNX code consumes `RgbImage` pixel buffers produced via ImageSharp decode (and letterbox resize for SCRFD) |
| Is ImageSharp “merely” preprocessing? | **Yes for ML** — decode + detector letterbox resize. Alignment crop uses custom `WarpAffine`, not ImageSharp |
| Can face recognition operate without ImageSharp *as coded today*? | **No** — removing the package without replacing decode/resize would break `RgbImage` compilation and all face flows |
| Would replacing ImageSharp affect face recognition? | **Yes until** decode + letterbox behavior is reimplemented equivalently (channel order RGB, triangle/bilinear letterbox matching InsightFace expectations). Wrong preprocess changes embeddings and match quality |

### Dependency chains (production)

**Validate photo**

`POST …/validate-photo` → `VisitorService.ValidateVisitorPhoto` → `MediaStorageService.IsAllowedImage` → `InsightFaceService.CountFaces` → `RgbImage.Decode` (**ImageSharp Load**) → `ScrfdDetector.Detect` → `LetterboxTopLeft` (**ImageSharp Resize**) → ONNX SCRFD → face count

**Register / save photo**

Registration with photo → `SavePhotoAsync` → `EnsureExactlyOneFace` (same decode/detect path) → `SaveVisitorPhotoAsync` (raw bytes) → `EnrolFaceAsync` → `Embed` → Decode + Detect + `ArcFaceEmbedder.Embed` (WarpAffine + ONNX)

**Returning visitor / face checkout**

`FaceSearch` / `FaceIdentifyInside` → `EmbedPhoto` → same Embed path → cosine match against stored descriptors

---

## 7. Git History

| Fact | Evidence |
|------|----------|
| Introduced | Commit **`7871d4f`** (2026-09-17) — “Move face recognition server-side with InsightFace ArcFace” |
| csproj blame | ImageSharp line attributed to `7871d4f6` |
| Feature that required it | Server-side SCRFD + ArcFace pipeline; replaced **browser face-api.js** descriptors (not a prior server ImageSharp stack) |
| Prior ImageSharp versions in repo | **None** — first and only version is **4.1.2** |
| Later version changes | **None** found (`git log -S"SixLabors.ImageSharp"` on csproj shows introduction only) |
| Co-introduced | `Microsoft.ML.OnnxRuntime` 1.20.1 and `backend/Services/Face/*` including `RgbImage.cs` |

---

## 8. Why Production Publish Currently Fails

Command in scope:

`dotnet publish backend -c Release -o .\deploy\backend`

### Blocking error

ImageSharp’s NuGet package imports `build/SixLabors.ImageSharp.targets`, which defines:

- Target: `SixLabors_ValidateLicense`
- Runs: `BeforeTargets="CoreCompile"`
- Condition: not a design-time build
- Task: `SixLabors.Licensing.ValidateLicenseTask`
- Looks for: `$(SixLaborsLicenseKey)`, or `$(SixLaborsLicenseFile)`, or recursive `**/sixlabors.lic`
- **`ContinueOnError="$(Configuration.StartsWith('Debug'))"`**

Therefore:

- **Debug:** missing license → task continues (warning/noise; build can succeed)
- **Release / production publish:** missing license → **compile fails** with:

  > No Six Labors license found.  
  > Set $(SixLaborsLicenseKey), $(SixLaborsLicenseFile), or add a 'sixlabors.lic' file.

This is a **build-time license gate**, not a runtime ImageSharp crash, and not caused by missing application source.

### Warnings (separate; not fixed in this audit)

| Item | Severity | Notes |
|------|----------|-------|
| MailKit 4.15.1 NU1902 | Moderate vulnerability advisory | Known; out of scope for this audit |
| Prior enterprise note | ImageSharp was listed as INFORMATIONAL “license warning” in Debug-oriented audits | Release elevates the same gap to a **blocker** |

---

## 9. Licensing Situation

Findings from **local** package `LICENSE` (Six Labors Split License 1.0) and build targets only — no external legal advice, no purchase recommendation.

| Topic | Finding |
|-------|---------|
| Exact version | 4.1.2 |
| Why build requires a license artifact | Package MSBuild `ValidateLicenseTask` enforces presence of key/file for non-Debug configurations |
| License config in this repo today | **Absent** |
| Direct vs transitive | **Direct** — Split License Apache path for “transitive only” consumers does **not** apply to this project’s relationship to ImageSharp |
| Split License (high level) | Apache 2.0 **or** Six Labors Commercial, depending on qualifying criteria stated in the package LICENSE (e.g. OSS consumption, transitive consumption, for-profit under revenue threshold, non-profit/charity, otherwise commercial) |
| Does failure prove commercial purchase is required? | **No** — it only proves the **build validation artifact** is missing. Which license grant applies is a separate eligibility determination |
| Was 4.x intentional? | Yes — introduced at 4.1.2 with the face feature; no accidental transitive pull |

---

## 10. Possible Options

| Option | Technical Impact | Licensing Impact | Risk | Work Required |
|--------|------------------|------------------|------|---------------|
| **A — Keep 4.1.2 + configure appropriate license** | No app behavior change; Release publish unblocked once key/file configured in build/CI secrets | Depends on eligibility (free/Apache-qualifying key vs commercial). Build still needs the artifact Six Labors tooling expects | Low technical risk if key valid; process/secret-management risk | Ops/legal: obtain correct key/file; wire `SixLaborsLicenseKey` / file into Release publish only — **no feature code required** |
| **B — Different ImageSharp version** | May change APIs/behavior; older major lines have different license models historically; must re-validate decode/letterbox and all face tests | Licensing may differ by major version; must be verified against that version’s LICENSE before choosing | Medium–high (match quality / API breaks / still needing a key on 3.x/4.x-style gates) | Package change + face regression suite + legal review of that version |
| **C — Replace with another .NET imaging library** | Reimplement `RgbImage.Decode` and `LetterboxTopLeft` (and test JPEG helpers) against another decoder/resizer; keep ONNX pipeline | Removes Six Labors Split License from **direct** dependency (confirm no transitive Six Labors) | Medium–high: wrong RGB/order or resize filter can silently degrade face match rates | Coding + ArcFacePipelineTests + SingleFaceCapture + revisit/checkout E2E |
| **D — Remove ImageSharp as unnecessary** | **Not viable as-is** — production face decode/letterbox depend on it | Would eliminate license gate only if dependency removed | **High / feature-breaking** for face gate, enrol, search, checkout-by-face unless replaced (Option C) or face features abandoned | Would require Option C or product decision to drop face features |

### Per-option notes (photos / face / deploy / commercial VMS)

| Option | Visitor photos (storage) | Face recognition | Production deploy | Commercial VMS fit |
|--------|--------------------------|------------------|-------------------|--------------------|
| A | Unchanged | Unchanged | Unblocks Release publish | Appropriate if license eligibility is settled and secrets are managed |
| B | Unchanged if API parity kept | Must re-prove embedding stability | May unblock or re-license differently | Only after legal+tech parity proof |
| C | Storage still independent | Works only after equivalent preprocess | Unblocks if Six Labors removed from Release graph | Common approach when avoiding vendor license gates |
| D without replacement | Storage OK; face gate may fail/open depending on follow-on changes | Breaks current pipeline | Publish may pass if package gone, product incomplete | Not appropriate while face is a product feature |

---

## 11. Security / Privacy Considerations

| Surface | ImageSharp involved? | Notes |
|---------|----------------------|-------|
| User-uploaded visitor images | **Yes (in memory)** | Decoded for face count/embed/match; not rewritten by ImageSharp before disk write |
| Private visitor media on disk | **No** | Stored/served as original bytes under tenant-private `App_Data/media/...` |
| Public/static files | **No** | Not used for wwwroot static branding via ImageSharp |
| Temporary files | **No dedicated ImageSharp temp files observed** | In-memory `Image<Rgb24>` during decode/resize |
| Security implications | Decoder attack surface on untrusted uploads; mitigated somewhat by size cap (5MB) and magic-byte check **before** some paths, but face paths still decode via ImageSharp | Replacing the library does not remove the need to decode untrusted images for face features |
| Privacy | Face templates derived after ImageSharp decode; photos remain private media | No change in this audit |

---

## 12. Recommendation for Decision

Do **not** purchase or configure anything automatically from this document.

Before choosing A/B/C/D, the team needs these **facts**:

1. **Product:** Is server-side face recognition (validate-photo, enrol, face search, face checkout) required for the first production cut?
2. **Legal/commercial eligibility:** Under the Six Labors Split License text shipped with 4.1.2, which grant applies to TIAANO’s entity (revenue, OSS posture, etc.)? Legal/owner decision — not an engineering assumption.
3. **Build ops:** If keeping 4.1.2, how will Release/CI supply `SixLaborsLicenseKey` or `sixlabors.lic` without committing secrets?
4. **Risk appetite:** If avoiding Six Labors, is the team willing to fund Option C (replace decode/letterbox) and re-run face regression tests before go-live?
5. **Timeline:** Is unblocking `dotnet publish -c Release` needed before or after face-feature parity work?

Engineering recommendation until those answers exist: keep investigating options; **do not** change packages, add license files, or alter face preprocess code in production branches.

---

## 14. Resolution (2026-09-19) — Option C implemented

### Decision

The team chose **Option C**: replace only the `RgbImage` decode/letterbox layer. No Six Labors license key or file was added.

### Replacement chosen

| Item | Detail |
|------|--------|
| Library | **SkiaSharp 3.119.0** (+ `SkiaSharp.NativeAssets.Win32` 3.119.0 for Windows/IIS) |
| License | **MIT** (nuspec `license type=expression`) |
| Why selected | Permissive commercial use; .NET 9 compatible; decodes JPEG/PNG/WebP (formats already accepted by `MediaStorageService`); no Release MSBuild license gate; widely used on Windows |
| Letterbox | **Managed bilinear** inside `RgbImage` (same sampler as existing `WarpAffine`), not SkiaSharp resize — preserves SCRFD letterbox geometry without ImageSharp Triangle |

### Old ImageSharp responsibility → new implementation

| Responsibility | Before | After |
|----------------|--------|-------|
| Decode upload bytes | `Image.Load<Rgb24>` | `SKBitmap.Decode` → packed RGB via `SKColor` R/G/B |
| SCRFD letterbox resize | ImageSharp `Mutate(Resize Triangle)` | `ResizeBilinear` + top-left paste on black canvas |
| ONNX SCRFD / ArcFace | Unchanged | Unchanged |
| Photo storage | Unchanged | Unchanged |

### Proof InsightFace/ArcFace unchanged

- `ScrfdDetector`, `ArcFaceEmbedder`, `InsightFaceService`, model files, thresholds: **not modified**
- Only `RgbImage.Decode` / `LetterboxTopLeft` internals changed; public `RgbImage` contract retained

### Proof multi-face rejection intact

- `FacePhotoRules.MultipleFacesMessage` / `MULTIPLE_FACES_DETECTED` unchanged
- `SingleFaceCaptureTests` (0 / 1 / 2+ / composite / EnsureExactlyOneFace) **passed**
- `RgbImagePreprocessTests.FacePhotoRules_MultiFace_Uses_Standard_Message_And_Code` **passed**

### Tests run (Release)

- `RgbImagePreprocessTests`, `SingleFaceCaptureTests`, `FaceGeometryTests`, `ArcFacePipelineTests`, `FaceRevisit*`, `TenantIsolationTests.Face_Search_Does_Not_Match_Across_Tenants`: **36 passed**

### Production build result

```
dotnet clean backend
dotnet restore backend
dotnet build backend -c Release     → succeeded (MailKit NU1902 warning only)
dotnet publish backend -c Release -o .\deploy\backend  → succeeded (no Six Labors license error)
```

### Package audit after change

```
dotnet list backend package --include-transitive
```

| Package | Status |
|---------|--------|
| `SixLabors.ImageSharp` | **Removed** — no longer referenced |
| `SkiaSharp` / `NativeAssets.Win32` | **Direct** — face decode |
| `SixLabors.Fonts` 1.0.0 | **Transitive via ClosedXML** — Apache-2.0; **no** ImageSharp license build task; not used by face pipeline |

### Remaining notes

- MailKit 4.15.1 NU1902 moderate advisory: **unchanged**, out of scope
- Some `VisitConcurrencyTests` fail when active feedback questions exist in the shared test DB (`EnsureFeedbackCompleteAsync`); **pre-existing** feedback gate, unrelated to this ImageSharp replacement; concurrency test sources were not modified

### Exact next decision (superseded)

Section 13’s “no code change” hold is **lifted** for Option C only. No Six Labors commercial license was purchased as part of this work.
