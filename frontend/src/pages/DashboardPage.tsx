import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { dashboardApi, apiErrorMessage } from '../lib/api'
import type { ChartPointDto, DashboardDto } from '../types/api'
import { Alert, Badge, EmptyState, KpiCard, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { formatDateTime, hasAnyRole, primaryRole, statusBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useNavigate } from 'react-router-dom'

function greetingForNow() {
  const h = new Date().getHours()
  if (h < 12) return 'Good morning'
  if (h < 17) return 'Good afternoon'
  return 'Good evening'
}

const PIE_COLORS = ['#008F95', '#35C7C7', '#006F75', '#F2A93B', '#7AD3C9', '#E4572E', '#54717a', '#B8E3DF']

function ChartCard({
  title,
  data,
  type,
  color,
}: {
  title: string
  data: ChartPointDto[]
  type: 'line' | 'bar' | 'pie'
  color: string
}) {
  const points = type === 'pie' ? (data ?? []).filter((p) => p.value > 0) : data ?? []
  return (
    <Panel className="min-w-0">
      <h2 className="mb-4 text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">{title}</h2>
      <div className="h-64 w-full min-w-0">
        {points.length === 0 ? (
          <div className="flex h-full items-center justify-center text-sm text-ink-muted">No data yet</div>
        ) : (
          <ResponsiveContainer width="100%" height="100%" minWidth={0}>
            {type === 'pie' ? (
              <PieChart>
                <Pie data={points} dataKey="value" nameKey="label" innerRadius="45%" outerRadius="75%" paddingAngle={2}>
                  {points.map((p, i) => (
                    <Cell key={p.label} fill={PIE_COLORS[i % PIE_COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip />
                <Legend verticalAlign="bottom" iconType="circle" wrapperStyle={{ fontSize: 12 }} />
              </PieChart>
            ) : type === 'line' ? (
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
    return <Spinner label="Loading dashboard…" />
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

  const momPendingCount = data.momPendingCount ?? 0
  const isSuperAdmin = hasAnyRole(user?.roles ?? [], ['SuperAdmin'])
  const showMomCard = hasAnyRole(user?.roles ?? [], ['SuperAdmin', 'Admin', 'Host'])
  const waivedCount = Math.max(0, (data.checkedOutWithoutMomCount ?? 0) - momPendingCount)
  const cardCount = 4 + (isSuperAdmin ? 1 : 0) + (showMomCard ? 1 : 0)
  const cardColumns = cardCount >= 6 ? 'xl:grid-cols-6' : cardCount === 5 ? 'xl:grid-cols-5' : 'xl:grid-cols-4'

  return (
    <div>
      <PageHeader
        eyebrow="Overview"
        title={`${greetingForNow()}${user?.fullName ? `, ${user.fullName.split(' ')[0]}` : ''}`}
        subtitle={
          data.isHostView
            ? "Here's today's activity for the visitors coming to meet you."
            : "Here's today's visitor activity across the facility."
        }
        actions={
          role === 'Reception' ? (
            <Button onClick={() => navigate('/reception')}>Reception Mode</Button>
          ) : undefined
        }
      />

      <div className={`grid gap-3 sm:grid-cols-2 lg:grid-cols-3 ${cardColumns}`}>
        <KpiCard label="Visitors Today" value={data.visitorsToday ?? 0} to="/visitors?quickFilter=today" icon="◎" />
        <KpiCard label="Currently Inside" value={data.currentlyInside ?? 0} to="/visitors/inside" icon="●" />
        <KpiCard label="Expected Today" value={data.expectedToday ?? 0} to="/visitors/expected" icon="◷" />
        <KpiCard
          label="Pending Approvals"
          value={data.pendingApprovals ?? 0}
          to="/host"
          icon="◇"
          hint={(data.pendingApprovals ?? 0) === 0 ? 'Clear' : 'Action'}
        />
        {isSuperAdmin ? (
          <KpiCard
            label="Manual check-outs today"
            value={data.manualCheckOutsToday ?? 0}
            to="/visitors/manual-checkouts"
            icon="➜"
          />
        ) : null}
        {showMomCard ? (
          <KpiCard
            label={data.isHostView ? 'Your visitors: MOM not sent' : 'Checked out, MOM not sent'}
            value={momPendingCount}
            to="/visitors/mom-pending"
            icon="✉"
            hint={momPendingCount === 0 ? 'Clear' : waivedCount > 0 ? `${waivedCount} not required` : 'Action'}
          />
        ) : null}
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-3">
        <ChartCard title="Visitor activity" data={data.dailyTrend} type="line" color="#008F95" />
        <ChartCard title="By department" data={data.byDepartment} type="bar" color="#35C7C7" />
        <ChartCard title="By purpose" data={data.byPurpose} type="pie" color="#006F75" />
      </div>

      <div className="mt-6 grid gap-4 xl:grid-cols-2">
        <Panel>
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
        <Panel>
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Recent visitor activity</h2>
            <Link to="/visitors" className="text-xs font-semibold text-primary hover:underline">
              View all
            </Link>
          </div>
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
