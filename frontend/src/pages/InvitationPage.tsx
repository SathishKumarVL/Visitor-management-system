import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { addDays, format } from 'date-fns'
import { BrandLogo } from '../components/BrandLogo'
import { FaceCapture } from '../components/FaceCapture'
import { Button } from '../components/ui/Button'
import { FieldLabel, SelectChip, TextInput, TextSelect } from '../components/ui/Field'
import { Alert, Spinner } from '../components/ui/Panel'
import { apiErrorMessage, publicInviteApi } from '../lib/api'
import type { PublicInvitationDto, PublicInvitationResultDto } from '../types/api'
import { formatDate, formatDateTime, isPastDateTime, nextBookingSlot, sanitizePhoneDigits, todayIsoDate } from '../lib/utils'

/** Must match VisitorInvitationService.MaxDaysAhead on the server. */
const MAX_DAYS_AHEAD = 90

/** Quarter-hour times, 00:00 to 23:45. */
const SLOTS = Array.from({ length: 96 }, (_, i) =>
  `${String(Math.floor(i / 4)).padStart(2, '0')}:${String((i % 4) * 15).padStart(2, '0')}`,
)

const isOthers = (name: string) => name.trim().toLowerCase() === 'others'

function Card({ title, subtitle, children }: { title: string; subtitle?: ReactNode; children: ReactNode }) {
  return (
    <div className="relative flex min-h-screen items-start justify-center bg-page px-4 py-8 sm:items-center sm:px-8">
      <div className="absolute inset-0 bg-brand-soft opacity-70" />
      <div className="relative w-full max-w-2xl rounded-3xl border border-border/80 bg-white p-6 shadow-elevated sm:p-8">
        <BrandLogo className="h-12" />
        <h1 className="mt-5 text-2xl font-semibold text-ink">{title}</h1>
        {subtitle ? <div className="mt-1 text-sm text-ink-muted">{subtitle}</div> : null}
        <div className="mt-6">{children}</div>
      </div>
    </div>
  )
}

/** Public page behind the emailed invitation link: the visitor fills in their details and face, which books the visit. */
export function InvitationPage() {
  // The token sits in the URL fragment, which browsers never send to the server or in the Referer header.
  const token = useMemo(() => decodeURIComponent(window.location.hash.replace(/^#/, '')).trim(), [])
  const [invitation, setInvitation] = useState<PublicInvitationDto | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [result, setResult] = useState<PublicInvitationResultDto | null>(null)

  const [gender, setGender] = useState<'' | 1 | 2>('')
  const [visitorName, setVisitorName] = useState('')
  const [companyName, setCompanyName] = useState('')
  const [phone, setPhone] = useState('')
  const [slot] = useState(nextBookingSlot)
  const [expectedDate, setExpectedDate] = useState(slot.date)
  const [expectedTime, setExpectedTime] = useState(slot.time)
  const [purposeIds, setPurposeIds] = useState<string[]>([])
  const [othersChecked, setOthersChecked] = useState(false)
  const [otherText, setOtherText] = useState('')
  const [photo, setPhoto] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [triedSubmit, setTriedSubmit] = useState(false)

  useEffect(() => {
    if (!token) {
      setLoadError('This invitation link is not complete. Please open the full link from the email.')
      return
    }
    publicInviteApi.open(token)
      .then(setInvitation)
      .catch((err) => setLoadError(apiErrorMessage(err)))
  }, [token])

  // Re-render every minute so times that have just passed become unavailable.
  const [, setTick] = useState(0)
  useEffect(() => {
    const timer = window.setInterval(() => setTick((n) => n + 1), 60_000)
    return () => window.clearInterval(timer)
  }, [])

  const today = todayIsoDate()
  const maxDate = format(addDays(new Date(), MAX_DAYS_AHEAD), 'yyyy-MM-dd')
  const regularPurposes = invitation?.purposes.filter((p) => !isOthers(p.name)) ?? []

  const problems = [
    gender ? null : 'Select Mr. or Ms.',
    visitorName.trim() ? null : 'Enter your name.',
    companyName.trim() ? null : 'Enter your company name.',
    sanitizePhoneDigits(phone).length === 10 ? null : 'Enter your 10-digit mobile number.',
    expectedDate && expectedDate >= today && expectedDate <= maxDate ? null : 'Choose a valid date.',
    isPastDateTime(expectedDate, expectedTime) ? 'The chosen time has already passed. Choose a later time.' : null,
    purposeIds.length > 0 || (othersChecked && otherText.trim()) ? null : 'Select the purpose of your visit.',
    othersChecked && !otherText.trim() ? 'Describe the purpose under Others.' : null,
    photo ? null : 'Take your face photo.',
  ].filter((p): p is string => !!p)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTriedSubmit(true)
    if (problems.length > 0) {
      setError(problems[0])
      return
    }
    setSubmitting(true)
    setError(null)
    try {
      const booked = await publicInviteApi.submit({
        token,
        visitorName: visitorName.trim(),
        companyName: companyName.trim(),
        gender: gender as 1 | 2,
        phone: sanitizePhoneDigits(phone),
        expectedDate,
        expectedTime,
        purposeIds,
        purposeNotes: othersChecked ? otherText.trim() : null,
        photoBase64: photo!,
      })
      setResult(booked)
      window.history.replaceState(null, '', window.location.pathname)
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSubmitting(false)
    }
  }

  if (result) {
    return (
      <Card title="Your appointment is booked" subtitle="A confirmation has been sent to your email.">
        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-ink-muted">Booking number</dt>
            <dd className="font-semibold text-ink">{result.visitNumber}</dd>
          </div>
          <div>
            <dt className="text-ink-muted">Meeting</dt>
            <dd className="font-semibold text-ink">{result.hostName}</dd>
          </div>
          <div>
            <dt className="text-ink-muted">When</dt>
            <dd className="font-semibold text-ink">
              {formatDate(result.visitDate)} {result.visitTime.slice(0, 5)}
            </dd>
          </div>
          {result.locationName ? (
            <div>
              <dt className="text-ink-muted">Where</dt>
              <dd className="font-semibold text-ink">{result.locationName}</dd>
            </div>
          ) : null}
        </dl>
        <p className="mt-6 text-sm text-ink-muted">
          At reception, your face is matched to the photo you just took, so check-in is quick. You can close this page.
        </p>
      </Card>
    )
  }

  if (loadError) {
    return (
      <Card title="Invitation unavailable">
        <Alert tone="error" persistent>{loadError}</Alert>
      </Card>
    )
  }

  if (!invitation) {
    return (
      <Card title="Opening your invitation…">
        <Spinner label="Loading…" />
      </Card>
    )
  }

  const where = [invitation.locationName, invitation.organisationName].filter(Boolean).join(', ')

  return (
    <Card
      title="Book your visit"
      subtitle={
        <>
          <span className="font-medium text-ink">{invitation.hostName}</span>
          {invitation.departmentName ? ` (${invitation.departmentName})` : ''} has invited you
          {where ? ` to ${where}` : ''}. Fill in your details below. This link works once and is valid until{' '}
          {formatDateTime(invitation.expiresAt)}.
        </>
      }
    >
      <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2" noValidate>
        <div className="sm:col-span-2">
          <FieldLabel htmlFor="inv-name">Your name *</FieldLabel>
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <div className="flex shrink-0 gap-2">
              <Button type="button" variant={gender === 1 ? 'primary' : 'secondary'} onClick={() => setGender(1)}>
                Mr.
              </Button>
              <Button type="button" variant={gender === 2 ? 'primary' : 'secondary'} onClick={() => setGender(2)}>
                Ms.
              </Button>
            </div>
            <TextInput
              id="inv-name"
              className="min-w-0 flex-1"
              autoComplete="name"
              maxLength={150}
              value={visitorName}
              onChange={(e) => setVisitorName(e.target.value)}
            />
          </div>
          {triedSubmit && !gender ? <p className="mt-1 text-xs font-medium text-danger">Select Mr. or Ms.</p> : null}
        </div>
        <div>
          <FieldLabel htmlFor="inv-company">Company *</FieldLabel>
          <TextInput
            id="inv-company"
            autoComplete="organization"
            maxLength={200}
            value={companyName}
            onChange={(e) => setCompanyName(e.target.value)}
          />
        </div>
        <div>
          <FieldLabel htmlFor="inv-phone">Mobile *</FieldLabel>
          <TextInput
            id="inv-phone"
            inputMode="numeric"
            autoComplete="tel-national"
            placeholder="10-digit number"
            maxLength={10}
            value={phone}
            onChange={(e) => setPhone(sanitizePhoneDigits(e.target.value))}
          />
        </div>
        <div className="sm:col-span-2">
          <FieldLabel htmlFor="inv-email">Email</FieldLabel>
          <TextInput id="inv-email" value={invitation.email} disabled readOnly />
        </div>
        <div>
          <FieldLabel htmlFor="inv-date">Visit date *</FieldLabel>
          <TextInput
            id="inv-date"
            type="date"
            min={today}
            max={maxDate}
            value={expectedDate}
            onChange={(e) => setExpectedDate(e.target.value)}
          />
        </div>
        <div>
          <FieldLabel htmlFor="inv-time">Time *</FieldLabel>
          <TextSelect id="inv-time" value={expectedTime} onChange={(e) => setExpectedTime(e.target.value)}>
            {SLOTS.map((s) => {
              const past = isPastDateTime(expectedDate, s)
              return (
                <option key={s} value={s} disabled={past}>
                  {s}
                </option>
              )
            })}
          </TextSelect>
        </div>
        <div className="sm:col-span-2">
          <FieldLabel>Purpose of visit *</FieldLabel>
          <div className="flex flex-wrap gap-2">
            {regularPurposes.map((p) => (
              <SelectChip
                key={p.id}
                selected={purposeIds.includes(p.id)}
                onToggle={() =>
                  setPurposeIds((ids) => (ids.includes(p.id) ? ids.filter((id) => id !== p.id) : [...ids, p.id]))
                }
              >
                {p.name}
              </SelectChip>
            ))}
            <SelectChip selected={othersChecked} onToggle={() => setOthersChecked((v) => !v)}>
              Others
            </SelectChip>
          </div>
          {othersChecked ? (
            <TextInput
              className="mt-2"
              placeholder="Describe the purpose of your visit"
              maxLength={500}
              value={otherText}
              onChange={(e) => setOtherText(e.target.value)}
            />
          ) : null}
        </div>
        <div className="sm:col-span-2">
          <FieldLabel>Face photo *</FieldLabel>
          <p className="mb-2 text-xs text-ink-muted">
            Look straight at the camera with your face inside the oval. Only you should be in the picture.
          </p>
          <div className="mx-auto max-w-sm">
            <FaceCapture
              photoUrl={photo}
              busy={submitting}
              captureLabel="Take photo"
              onCapture={(jpeg) => {
                setPhoto(jpeg)
                setError(null)
              }}
            />
            {photo ? (
              <Button
                type="button"
                variant="secondary"
                className="mt-2 w-full"
                disabled={submitting}
                onClick={() => setPhoto(null)}
              >
                Retake photo
              </Button>
            ) : null}
          </div>
        </div>
        {error ? (
          <div className="sm:col-span-2">
            <Alert tone="error" persistent>{error}</Alert>
          </div>
        ) : null}
        <div className="sm:col-span-2">
          <Button type="submit" variant="amber" className="w-full" disabled={submitting} loading={submitting}>
            {submitting ? 'Booking…' : 'Submit and book my visit'}
          </Button>
        </div>
      </form>
    </Card>
  )
}
