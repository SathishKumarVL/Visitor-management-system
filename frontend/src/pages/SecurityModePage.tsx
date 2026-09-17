import { useNavigate } from 'react-router-dom'
import { ModeTile, PageHeader, Panel } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'

export function SecurityModePage() {
  const navigate = useNavigate()
  return (
    <div>
      <PageHeader
        eyebrow="Security operations"
        title="Security Desk"
        subtitle="Fast check-in, verification, and on-site visibility."
      />

      <div className="mb-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Panel className="bg-brand-soft">
          <p className="text-xs font-semibold uppercase tracking-[0.12em] text-ink-muted">Focus</p>
          <p className="mt-2 text-lg font-semibold text-ink">Live gate activity</p>
        </Panel>
        <Panel>
          <p className="text-xs font-semibold uppercase tracking-[0.12em] text-ink-muted">Primary</p>
          <p className="mt-2 text-lg font-semibold text-ink">Verify by visit number</p>
        </Panel>
        <Panel>
          <p className="text-xs font-semibold uppercase tracking-[0.12em] text-ink-muted">Inside</p>
          <p className="mt-2 text-lg font-semibold text-ink">Monitor on-site guests</p>
        </Panel>
        <Panel>
          <p className="text-xs font-semibold uppercase tracking-[0.12em] text-ink-muted">Alerts</p>
          <p className="mt-2 text-lg font-semibold text-ink">Watch long stays</p>
        </Panel>
      </div>

      <div className="mb-6">
        <Button size="lg" className="min-h-16 w-full sm:w-auto" onClick={() => navigate('/verify')}>
          VERIFY VISITOR
        </Button>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <ModeTile title="EMERGENCY MODE" description="Evacuation roster for people inside" accent icon="!" onClick={() => navigate('/emergency')} />
        <ModeTile title="CHECK IN" description="Open expected arrivals" icon="→" onClick={() => navigate('/visitors/expected')} />
        <ModeTile title="CHECK OUT" description="Face match or list checkout" accent icon="✓" onClick={() => navigate('/checkout/face')} />
        <ModeTile title="VERIFY VISITOR" description="Look up by visit number" icon="◎" onClick={() => navigate('/verify')} />
        <ModeTile title="CURRENTLY INSIDE" description="Live on-site list" icon="●" onClick={() => navigate('/visitors/inside')} />
        <ModeTile title="SEARCH" description="Find a visitor" icon="🔍" onClick={() => navigate('/visitors')} />
        <ModeTile title="VISITOR PASSES" description="Issue or reprint passes" icon="▣" onClick={() => navigate('/passes')} />
      </div>
    </div>
  )
}
