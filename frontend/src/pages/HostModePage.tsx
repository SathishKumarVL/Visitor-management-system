import { useNavigate } from 'react-router-dom'
import { ModeTile, PageHeader } from '../components/ui/Panel'
import { useI18n } from '../i18n'

export function HostModePage() {
  const navigate = useNavigate()
  const { t } = useI18n()

  return (
    <div>
      <PageHeader
        eyebrow={t('host.eyebrow')}
        title={t('host.title')}
        subtitle={t('host.subtitle')}
      />
      <div className="mx-auto grid max-w-4xl gap-4 pt-2 sm:grid-cols-2">
        <ModeTile
          className="min-h-40"
          title={t('host.expected')}
          description={t('host.expectedDesc')}
          accent
          icon="📅"
          onClick={() => navigate('/visitors/expected')}
        />
        <ModeTile
          className="min-h-40"
          title={t('host.inside')}
          description={t('host.insideDesc')}
          icon="●"
          onClick={() => navigate('/visitors/inside')}
        />
        <ModeTile
          className="min-h-36 sm:col-span-2"
          title={t('host.allVisitors')}
          description={t('host.allVisitorsDesc')}
          icon="🔍"
          onClick={() => navigate('/visitors')}
        />
      </div>
    </div>
  )
}
