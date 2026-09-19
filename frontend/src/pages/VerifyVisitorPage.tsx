import { useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, passApi, visitorsApi } from '../lib/api'
import type { PassDto } from '../types/api'
import { Alert, PageHeader, Panel } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'
import { formatDateTime } from '../lib/utils'
import { SecureImage } from '../components/SecureImage'

function statusKey(status: string): string {
  return status.toLowerCase()
}

export function VerifyVisitorPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const checkoutMode = searchParams.get('mode') === 'checkout'
  const [visitNumber, setVisitNumber] = useState('')
  const [pass, setPass] = useState<PassDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleVerify(code: string) {
    const normalized = code.trim()
    if (!normalized || busy) return
    setBusy(true)
    setError(null)
    setMessage(null)
    try {
      const result = await passApi.verify(normalized)
      setPass(result)
      const status = statusKey(result.status)

      if (checkoutMode) {
        if (status.includes('inside')) {
          await visitorsApi.checkOut(result.visitId)
          setPass(null)
          setMessage(`Checked out: ${result.visitorName}. Out time recorded.`)
        } else if (status.includes('approved') || status.includes('expected')) {
          setMessage(`${result.visitorName} is not checked in yet. Check in first, then use Check Out.`)
        } else if (status.includes('checkedout') || status.includes('checked out')) {
          setMessage(`Already checked out: ${result.visitorName}`)
        } else {
          setMessage(`Cannot check out ${result.visitorName} (status: ${result.status}).`)
        }
      } else if (status.includes('inside')) {
        setMessage(`Verified on site: ${result.visitorName}`)
      } else if (status.includes('approved') || status.includes('expected')) {
        const updated = await visitorsApi.checkIn(result.visitId)
        setPass(updated)
        setMessage(`Checked in: ${result.visitorName}`)
      } else if (status.includes('checkedout') || status.includes('checked out')) {
        setMessage(`Visitor already checked out: ${result.visitorName}`)
      } else {
        setMessage(`Verified: ${result.visitorName} (${result.status})`)
      }
    } catch (e) {
      setPass(null)
      setError(apiErrorMessage(e, 'Visitor not found. Check the visit number and try again.'))
    } finally {
      setBusy(false)
    }
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    void handleVerify(visitNumber)
  }

  return (
    <div>
      <PageHeader
        eyebrow="Security operations"
        title={checkoutMode ? 'Check Out by Visit Number' : 'Verify Visitor'}
        subtitle="Look up visitors by the visit number printed on their pass. No QR codes."
      />

      <Panel className="mx-auto max-w-xl">
        <form onSubmit={onSubmit} className="space-y-4">
          <div>
            <FieldLabel htmlFor="visitNumber">Visit number</FieldLabel>
            <TextInput
              id="visitNumber"
              value={visitNumber}
              onChange={(e) => setVisitNumber(e.target.value)}
              placeholder="e.g. VMS-2026-000184"
              autoComplete="off"
              autoFocus
              className="font-mono"
            />
          </div>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={busy || !visitNumber.trim()}>
              {busy ? 'Working…' : checkoutMode ? 'Check out' : 'Verify'}
            </Button>
            <Button type="button" variant="secondary" onClick={() => navigate('/visitors')}>
              Search visitors
            </Button>
            <Button type="button" variant="ghost" onClick={() => navigate('/visitors/inside')}>
              Currently inside
            </Button>
          </div>
        </form>

        {error ? (
          <div className="mt-4">
            <Alert tone="error">{error}</Alert>
          </div>
        ) : null}
        {message ? (
          <div className="mt-4">
            <Alert tone="success">{message}</Alert>
          </div>
        ) : null}

        {pass ? (
          <div className="mt-6 grid gap-4 border-t border-border pt-4 sm:grid-cols-[1fr_120px]">
            <div className="space-y-1 text-sm">
              <div className="font-mono text-lg font-semibold text-primary">{pass.visitNumber}</div>
              <div className="text-lg font-semibold text-ink">{pass.visitorName}</div>
              <div className="text-ink-muted">{pass.companyName}</div>
              <div>Person to meet: {pass.hostName}</div>
              <div>Department: {pass.departmentName}</div>
              <div>Status: {pass.status}</div>
              <div>Check-in: {formatDateTime(pass.checkInAt)}</div>
              <div className="mt-3 flex flex-wrap gap-2">
                <Button size="sm" variant="secondary" onClick={() => navigate(`/visitors/${pass.visitId}`)}>
                  Open detail
                </Button>
                {statusKey(pass.status).includes('inside') ? (
                  <Button
                    size="sm"
                    disabled={busy}
                    onClick={() => void handleVerify(pass.visitNumber)}
                  >
                    Record check-out
                  </Button>
                ) : null}
              </div>
            </div>
            {pass.photoUrl ? (
              <SecureImage
                src={pass.photoUrl}
                alt={pass.visitorName}
                className="h-28 w-28 rounded-2xl object-cover"
              />
            ) : (
              <div className="flex h-28 w-28 items-center justify-center rounded-2xl bg-mint text-xs text-ink-muted">
                No photo
              </div>
            )}
          </div>
        ) : null}
      </Panel>
    </div>
  )
}
