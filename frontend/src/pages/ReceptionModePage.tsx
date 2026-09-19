import { useLocation, useNavigate } from 'react-router-dom'
import { BrandLogo } from '../components/BrandLogo'
import { Alert, ModeTile } from '../components/ui/Panel'
import { useSettingsStore } from '../store/settingsStore'
import { useI18n } from '../i18n'

export function ReceptionModePage() {
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useI18n()
  const company = useSettingsStore((s) => s.settings.companyName)
  const registration = location.state as
    | { visitorName?: string; pendingApproval?: boolean; visitNumber?: string }
    | null
  const registeredName = registration?.visitorName

  return (
    <div className="mx-auto max-w-5xl">
      <div className="relative overflow-hidden rounded-[1.75rem] border border-border/70 bg-white p-6 shadow-elevated sm:p-10">
        <div className="pointer-events-none absolute -right-10 -top-10 h-44 w-44 rounded-full bg-aqua-light/80" />
        <div className="pointer-events-none absolute -bottom-16 left-10 h-36 w-36 rounded-full bg-mint" />

        <div className="relative text-center">
          <BrandLogo className="mx-auto h-12" />
          <p className="mt-5 text-xs font-semibold uppercase tracking-[0.22em] text-primary">{t('reception.eyebrow')}</p>
          <h1 className="mt-3 text-3xl font-semibold tracking-tight text-ink sm:text-4xl">{t('reception.welcome')}</h1>
          <p className="mt-2 text-sm text-ink-muted">
            {t('reception.subtitle', '{company} reception desk — choose an action to continue').replace(
              '{company}',
              company || t('nav.visitorManagement'),
            )}
          </p>
        </div>

        {registeredName ? (
          <div className="relative mx-auto mt-6 max-w-xl" role="status" aria-live="polite">
            {registration?.pendingApproval ? (
              <Alert tone="warning">
                {t('reception.pendingApproval')
                  .replace('{name}', registeredName)
                  .replace('{visit}', registration.visitNumber ? ` ${registration.visitNumber}` : '')}
              </Alert>
            ) : (
              <Alert tone="success">
                {t('reception.registeredOk').replace('{name}', registeredName)}
              </Alert>
            )}
          </div>
        ) : null}

        <div className="relative mx-auto mt-8 grid max-w-3xl gap-4 sm:grid-cols-2">
          <ModeTile
            className="min-h-40 sm:col-span-2 sm:items-center sm:text-center"
            title={t('reception.newVisitor')}
            description={t('reception.newVisitorDesc')}
            accent
            icon="👤"
            onClick={() => navigate('/visitors/new')}
          />
          <ModeTile
            className="min-h-36"
            title={t('reception.expectedVisitor')}
            description={t('reception.expectedVisitorDesc')}
            icon="📅"
            onClick={() => navigate('/visitors/expected')}
          />
          <ModeTile
            className="min-h-36"
            title={t('reception.searchVisitor')}
            description={t('reception.searchVisitorDesc')}
            icon="🔍"
            onClick={() => navigate('/visitors')}
          />
          <ModeTile
            className="min-h-36"
            title={t('reception.checkOut')}
            description={t('reception.checkOutDesc')}
            icon="➜"
            onClick={() => navigate('/checkout/face')}
          />
          <ModeTile
            className="min-h-36"
            title={t('reception.verifyVisitor')}
            description={t('reception.verifyVisitorDesc')}
            icon="✓"
            onClick={() => navigate('/verify')}
          />
        </div>
      </div>
    </div>
  )
}
