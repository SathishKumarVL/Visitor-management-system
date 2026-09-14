import { useNavigate } from 'react-router-dom'
import { ModeTile, PageHeader } from '../components/ui/Panel'

export function HostModePage() {
  const navigate = useNavigate()

  return (
    <div>
      <PageHeader
        eyebrow="Host desk"
        title="Your visitors"
        subtitle="See scheduled and on-site guests meeting you."
      />
      <div className="mx-auto grid max-w-4xl gap-4 pt-2 sm:grid-cols-2">
        <ModeTile
          className="min-h-40"
          title="EXPECTED VISITORS"
          description="See scheduled visitors for today"
          accent
          icon="📅"
          onClick={() => navigate('/visitors/expected')}
        />
        <ModeTile
          className="min-h-40"
          title="CURRENTLY INSIDE"
          description="Visitors currently on site"
          icon="●"
          onClick={() => navigate('/visitors/inside')}
        />
        <ModeTile
          className="min-h-36 sm:col-span-2"
          title="ALL VISITORS"
          description="Search the full visitor list"
          icon="🔍"
          onClick={() => navigate('/visitors')}
        />
      </div>
    </div>
  )
}
