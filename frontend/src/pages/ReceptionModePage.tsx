import { useLocation, useNavigate } from 'react-router-dom'
import { BrandLogo } from '../components/BrandLogo'
import { Alert, ModeTile } from '../components/ui/Panel'
import { useSettingsStore } from '../store/settingsStore'

export function ReceptionModePage() {
  const navigate = useNavigate()
  const location = useLocation()
  const company = useSettingsStore((s) => s.settings.companyName)
  const registeredName = (location.state as { visitorName?: string } | null)?.visitorName

  return (
    <div className="mx-auto max-w-5xl">
      <div className="relative overflow-hidden rounded-[1.75rem] border border-border/70 bg-white p-6 shadow-elevated sm:p-10">
        <div className="pointer-events-none absolute -right-10 -top-10 h-44 w-44 rounded-full bg-aqua-light/80" />
        <div className="pointer-events-none absolute -bottom-16 left-10 h-36 w-36 rounded-full bg-mint" />

        <div className="relative text-center">
          <BrandLogo className="mx-auto h-12" />
          <p className="mt-5 text-xs font-semibold uppercase tracking-[0.22em] text-primary">Visitor Management</p>
          <h1 className="mt-3 text-3xl font-semibold tracking-tight text-ink sm:text-4xl">Welcome</h1>
          <p className="mt-2 text-sm text-ink-muted">
            {company} reception desk — choose an action to continue
          </p>
        </div>

        {registeredName ? (
          <div className="relative mx-auto mt-6 max-w-xl">
            <Alert tone="success">Visitor “{registeredName}” registered successfully.</Alert>
          </div>
        ) : null}

        <div className="relative mx-auto mt-8 grid max-w-3xl gap-4 sm:grid-cols-2">
          <ModeTile
            className="min-h-40 sm:col-span-2 sm:items-center sm:text-center"
            title="NEW VISITOR"
            description="Register a walk-in visitor now"
            accent
            icon="👤"
            onClick={() => navigate('/visitors/new')}
          />
          <ModeTile
            className="min-h-36"
            title="EXPECTED VISITOR"
            description="Open booked appointments"
            icon="📅"
            onClick={() => navigate('/visitors/expected')}
          />
          <ModeTile
            className="min-h-36"
            title="SEARCH VISITOR"
            description="Find a visitor by name or company"
            icon="🔍"
            onClick={() => navigate('/visitors')}
          />
          <ModeTile
            className="min-h-36"
            title="CHECK OUT"
            description="Face match or list checkout"
            icon="➜"
            onClick={() => navigate('/checkout/face')}
          />
          <ModeTile
            className="min-h-36"
            title="VERIFY VISITOR"
            description="Look up by visit number on the pass"
            icon="✓"
            onClick={() => navigate('/verify')}
          />
        </div>
      </div>
    </div>
  )
}
