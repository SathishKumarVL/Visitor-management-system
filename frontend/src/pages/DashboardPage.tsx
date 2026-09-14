import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { dashboardApi, apiErrorMessage } from '../lib/api'
import type { ChartPointDto, DashboardDto } from '../types/api'
import { Alert, Badge, EmptyState, KpiCard, PageHeader, Panel, Skeleton, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { formatDateTime, primaryRole, statusBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useNavigate } from 'react-router-dom'

function greetingForNow() {
  const h = new Date().getHours()
  if (h < 12) return 'Good morning'
  if (h < 17) return 'Good afternoon'
  return 'Good evening'
}

function ChartCard({
  title,
  data,
  type,
  color,
}: {
  title: string
  data: ChartPointDto[]
  type: 'line' | 'bar'
  color: string
}) {
  const points = data ?? []
  return (
    <Panel className="min-w-0">
      <h2 className="mb-4 text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">{title}</h2>
      <div className="h-64 w-full min-w-0">
        {points.length === 0 ? (
          <div className="flex h-full items-center justify-center text-sm text-ink-muted">No data yet</div>
        ) : (
          <ResponsiveContainer width="100%" height="100%" minWidth={0}>
            {type === 'line' ? (
              <LineChart data={points}>
                <CartesianGrid strokeDasharray="3 3" stroke="#d4e8e6" />
                <XAxis dataKey="label" tick={{ fontSize: 12, fill: '#54717a' }} />
                <YAxis allowDecimals={false} tick={{ fontSize: 12, fill: '#54717a' }} />
                <Tooltip />
                <Line type="monotone" dataKey="value" stroke={color} strokeWidth={2.5} dot={{ r: 3 }} />
              </LineChart>
            ) : (
              <BarChart data={points}>
                <CartesianGrid strokeDasharray="3 3" stroke="#d4e8e6" />
                <XAxis dataKey="label" tick={{ fontSize: 11, fill: '#54717a' }} />
                <YAxis allowDecimals={false} tick={{ fill: '#54717a' }} />
                <Tooltip />
                <Bar dataKey="value" fill={color} radius={[8, 8, 0, 0]} />
              </BarChart>
            )}
          </ResponsiveContainer>
        )}
      </div>
    </Panel>
  )
}

export function DashboardPage() {
  const navigate = useNavigate()
  const user = useAuthStore((s) => s.user)
  const role = primaryRole(user?.roles ?? [])
  const [data, setData] = useState<DashboardDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setData(await dashboardApi.get())
    } catch (e) {
      setData(null)
      setError(apiErrorMessage(e, 'Unable to load dashboard. Check your connection and try again.'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    void (async () => {
      setLoading(true)
      setError(null)
      try {
        const result = await dashboardApi.get()
        if (!cancelled) setData(result)
      } catch (e) {
        if (!cancelled) {
          setData(null)
          setError(apiErrorMessage(e, 'Unable to load dashboard. Check your connection and try again.'))
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  if (loading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-16 w-full max-w-lg" />
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-32" />
          ))}
        </div>
        <Spinner label="Loading dashboard…" />
      </div>
    )
  }

  if (error) {
    return (
      <div className="space-y-3">
        <Alert tone="error">{error}</Alert>
        <Button onClick={() => void load()}>Retry</Button>
      </div>
    )
  }

  if (!data) {
    return (
      <div className="space-y-3">
        <Alert tone="warning">No dashboard data returned.</Alert>
        <Button onClick={() => void load()}>Retry</Button>
      </div>
    )
  }

  return (
    <div>
      <PageHeader
        eyebrow="Overview"
        title={`${greetingForNow()}${user?.fullName ? `, ${user.fullName.split(' ')[0]}` : ''}`}
        subtitle="Here's today's visitor activity across the facility."
        actions={
          role === 'Reception' ? (
            <Button onClick={() => navigate('/reception')}>Reception Mode</Button>
          ) : undefined
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Visitors Today" value={data.visitorsToday ?? 0} to="/visitors?quickFilter=today" icon="👤" />
        <KpiCard label="Currently Inside" value={data.currentlyInside ?? 0} to="/visitors/inside" icon="●" hint="Live" />
        <KpiCard label="Expected Today" value={data.expectedToday ?? 0} to="/visitors/expected" icon="◷" />
        <KpiCard
          label="Pending Approvals"
          value={data.pendingApprovals ?? 0}
          to="/host"
          icon="◇"
          hint={(data.pendingApprovals ?? 0) === 0 ? 'Clear' : 'Action'}
        />
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-5">
        <div className="xl:col-span-3">
          <ChartCard title="Visitor activity" data={data.dailyTrend} type="line" color="#008F95" />
        </div>
        <Panel className="xl:col-span-2">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Currently inside</h2>
            <Link to="/visitors/inside" className="text-xs font-semibold text-primary hover:underline">
              View all
            </Link>
          </div>
          <div className="space-y-2">
            {(data.currentlyInsideItems ?? []).length === 0 ? (
              <EmptyState title="No visitors currently inside" description="Checked-in visitors will appear here." />
            ) : (
              data.currentlyInsideItems.map((v) => (
                <Link
                  key={v.visitId}
                  to={`/visitors/${v.visitId}`}
                  className="flex min-h-12 items-center justify-between rounded-xl px-3 py-2 hover:bg-mint"
                >
                  <div>
                    <div className="font-medium text-ink">{v.visitorName}</div>
                    <div className="text-xs text-ink-muted">
                      {v.hostName} · {v.departmentName}
                    </div>
                  </div>
                  {v.isLongStay ? <Badge className="bg-red-100 text-danger ring-1 ring-red-200">Long stay</Badge> : null}
                </Link>
              ))
            )}
          </div>
        </Panel>
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        <ChartCard title="By department" data={data.byDepartment} type="bar" color="#35C7C7" />
        <ChartCard title="By purpose" data={data.byPurpose} type="bar" color="#006F75" />
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        <Panel>
          <h2 className="mb-3 text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Expected visitors</h2>
          <p className="mb-3 text-3xl font-semibold text-ink">{data.expectedToday ?? 0}</p>
          <Link to="/visitors/expected" className="text-sm font-semibold text-primary hover:underline">
            Open expected list →
          </Link>
        </Panel>
        <Panel>
          <h2 className="mb-3 text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Recent visitor activity</h2>
          <div className="space-y-2">
            {(data.recentVisitors ?? []).length === 0 ? (
              <p className="text-sm text-ink-muted">No visitors yet. Register one from Reception.</p>
            ) : (
              data.recentVisitors.map((v) => (
                <Link
                  key={v.visitId}
                  to={`/visitors/${v.visitId}`}
                  className="flex min-h-12 items-center justify-between rounded-xl px-3 py-2 hover:bg-mint"
                >
                  <div>
                    <div className="font-medium text-ink">{v.visitorName}</div>
                    <div className="text-xs text-ink-muted">
                      {v.companyName} · {formatDateTime(v.checkInAt)}
                    </div>
                  </div>
                  <Badge className={statusBadgeClass(String(v.statusLabel ?? v.status))}>
                    {v.statusLabel ?? String(v.status)}
                  </Badge>
                </Link>
              ))
            )}
          </div>
        </Panel>
      </div>
    </div>
  )
}
