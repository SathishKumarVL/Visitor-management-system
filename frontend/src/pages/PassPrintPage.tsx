import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiErrorMessage, passApi } from '../lib/api'
import type { PassDto } from '../types/api'
import { Alert, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { formatDateTime } from '../lib/utils'
import { BrandLogo } from '../components/BrandLogo'
import { SecureImage } from '../components/SecureImage'
import { useSettingsStore } from '../store/settingsStore'

export function PassPrintPage() {
  const { visitId } = useParams<{ visitId: string }>()
  const company = useSettingsStore((s) => s.settings.companyName)
  const [pass, setPass] = useState<PassDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!visitId) return
    void (async () => {
      try {
        setPass(await passApi.get(visitId))
      } catch (e) {
        setError(apiErrorMessage(e, 'Unable to load visitor pass.'))
      } finally {
        setLoading(false)
      }
    })()
  }, [visitId])

  if (loading) return <Spinner />
  if (error) return <Alert tone="error">{error}</Alert>
  if (!pass) return null

  return (
    <div>
      <div className="no-print mb-4 flex flex-wrap gap-2">
        <Button onClick={() => window.print()}>Print</Button>
        <Link to={`/visitors/${visitId}`}>
          <Button variant="secondary">Back to visitor</Button>
        </Link>
        <Link to="/passes">
          <Button variant="ghost">All passes</Button>
        </Link>
      </div>

      <Panel className="visitor-pass-print mx-auto max-w-xl overflow-hidden border-primary/25 print:border print:shadow-none">
        <div className="no-print -mx-4 -mt-4 mb-4 bg-brand-gradient px-5 py-4 text-white sm:-mx-5 sm:-mt-5 print:hidden">
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-white/80">Access badge</p>
          <p className="text-lg font-semibold">{company}</p>
        </div>

        <div className="flex items-start justify-between gap-4 border-b border-border pb-4">
          <div>
            <BrandLogo className="h-12" />
            <div className="mt-2 text-xs font-semibold uppercase tracking-[0.2em] text-ink-muted">Visitor Pass</div>
            <div className="text-sm text-ink-muted">{company}</div>
          </div>
          <div className="rounded-xl bg-brand-gradient px-3 py-2 text-center font-mono text-sm font-bold text-white">
            <div className="text-[10px] font-semibold uppercase tracking-wide text-white/80">Visit #</div>
            {pass.visitNumber}
          </div>
        </div>

        <div className="mt-4 grid gap-4 sm:grid-cols-[1fr_140px]">
          <div className="space-y-2 text-sm">
            <Row label="Visitor" value={pass.visitorName} />
            <Row label="Company" value={pass.companyName} />
            <Row label="Person to meet" value={pass.hostName} />
            <Row label="Department" value={pass.departmentName} />
            <Row label="Visit #" value={pass.visitNumber} />
            <Row label="Check-in" value={formatDateTime(pass.checkInAt)} />
            <Row label="Purpose" value={pass.purposes.join(', ') || '—'} />
            <Row label="Locations" value={pass.locations.join(', ') || '—'} />
            <Row label="Status" value={pass.status} />
          </div>
          <div className="flex flex-col items-center gap-3">
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
            <div className="w-full rounded-xl bg-mint/60 px-3 py-3 text-center">
              <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-ink-muted">Visit number</div>
              <div className="mt-1 break-all font-mono text-sm font-bold text-ink">{pass.visitNumber}</div>
            </div>
          </div>
        </div>
      </Panel>
    </div>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-[110px_1fr] gap-2">
      <div className="text-ink-muted">{label}</div>
      <div className="font-medium text-ink">{value}</div>
    </div>
  )
}
