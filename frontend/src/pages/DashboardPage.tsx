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
  const showMomPanel =
    !isSuperAdmin &&
    hasAnyRole(user?.roles ?? [], ['Admin', 'Host']) &&
    (data.isHostView || momPendingCount > 0)
  const withoutMomCount = data.checkedOutWithoutMomCount ?? 0
  const withoutMomItems = data.checkedOutWithoutMomItems ?? []
  const waivedCount = Math.max(0, withoutMomCount - momPendingCount)

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

      <div className={role === 'SuperAdmin' ? 'grid gap-3 sm:grid-cols-2 xl:grid-cols-5' : 'grid gap-3 sm:grid-cols-2 xl:grid-cols-4'}>
        <KpiCard label="Visitors Today" value={data.visitorsToday ?? 0} to="/visitors?quickFilter=today" icon="◎" />
        <KpiCard label="Currently Inside" value={data.currentlyInside ?? 0} to="/visitors/inside" icon="●" hint="Live" />
        <KpiCard label="Expected Today" value={data.expectedToday ?? 0} to="/visitors/expected" icon="◷" />
        <KpiCard
          label="Pending Approvals"
          value={data.pendingApprovals ?? 0}
          to="/host"
          icon="◇"
          hint={(data.pendingApprovals ?? 0) === 0 ? 'Clear' : 'Action'}
        />
        {role === 'SuperAdmin' ? (
          <KpiCard
            label="Manual check-outs today"
            value={data.manualCheckOutsToday ?? 0}
            to="/visitors?quickFilter=manualcheckouttoday"
            icon="➜"
            hint="SuperAdmin"
          />
        ) : null}
      </div>

      {showMomPanel ? (
        <Panel className="mt-6">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                Minutes of meeting pending
              </h2>
              <p className="text-xs text-ink-muted">
                {data.isHostView ? 'Your visitors who have left' : 'Visitors who have left'} without minutes of meeting.
                The check-out email is sent once the minutes are saved.
              </p>
            </div>
            <Badge
              className={
                momPendingCount > 0
                  ? 'bg-amber-100 text-amber-800 ring-1 ring-amber-200'
                  : 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200'
              }
            >
              {momPendingCount > 0 ? `${momPendingCount} pending` : 'All done'}
            </Badge>
          </div>
          {momPendingCount === 0 ? (
            <EmptyState title="No minutes pending" description="Minutes have been added for every visit." />
          ) : (
            <div className="space-y-2">
              {(data.momPendingItems ?? []).map((v) => (
                <div
                  key={v.visitId}
                  className="flex min-h-12 flex-wrap items-center justify-between gap-2 rounded-xl px-3 py-2 hover:bg-mint"
                >
                  <div>
                    <div className="font-medium text-ink">{v.visitorName}</div>
                    <div className="text-xs text-ink-muted">
                      {[v.companyName, data.isHostView ? null : v.hostName, v.checkOutAt ? `Left ${formatDateTime(v.checkOutAt)}` : null]
                        .filter(Boolean)
                        .join(' · ')}
                    </div>
                  </div>
                  <Button size="sm" onClick={() => navigate(`/visitors/${v.visitId}`)}>Add minutes</Button>
                </div>
              ))}
              {momPendingCount > (data.momPendingItems ?? []).length ? (
                <p className="px-3 text-xs text-ink-muted">
                  Showing the latest {(data.momPendingItems ?? []).length} of {momPendingCount}. Filter checked-out visits in{' '}
                  <Link to="/visitors" className="font-semibold text-primary hover:underline">Visitors</Link> to see the rest.
                </p>
              ) : null}
            </div>
          )}
        </Panel>
      ) : null}

      {isSuperAdmin ? (
        <Panel className="mt-6">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                Checked out without minutes of meeting
              </h2>
              <p className="text-xs text-ink-muted">
                Visitors who left with no minutes: still pending, or marked as not required.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Badge className="bg-amber-100 text-amber-800 ring-1 ring-amber-200">{momPendingCount} pending</Badge>
              <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">{waivedCount} not required</Badge>
            </div>
          </div>
          {withoutMomItems.length === 0 ? (
            <EmptyState title="Nothing to review" description="Every checked-out visit has minutes of meeting." />
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full text-left text-sm">
                <thead className="text-xs uppercase tracking-wide text-ink-muted">
                  <tr>
                    <th className="px-3 py-2">Visitor</th>
                    <th className="px-3 py-2">Person to meet</th>
                    <th className="px-3 py-2">Checked out</th>
                    <th className="px-3 py-2">MoM</th>
                    <th className="px-3 py-2" />
                  </tr>
                </thead>
                <tbody>
                  {withoutMomItems.map((v) => (
                    <tr key={v.visitId} className="border-t border-gray-100">
                      <td className="px-3 py-2">
                        <Link to={`/visitors/${v.visitId}`} className="font-medium text-ink hover:text-primary">
                          {v.visitorName}
                        </Link>
                        <div className="text-xs text-ink-muted">{[v.companyName, v.visitNumber].filter(Boolean).join(' · ')}</div>
                      </td>
                      <td className="px-3 py-2">
                        {v.hostName}
                        <div className="text-xs text-ink-muted">{v.departmentName}</div>
                      </td>
                      <td className="px-3 py-2 whitespace-nowrap">{formatDateTime(v.checkOutAt)}</td>
                      <td className="px-3 py-2">
                        {v.momNotRequired ? (
                          <>
                            <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">Not required</Badge>
                            <div className="mt-0.5 text-xs text-ink-muted">
                              {[v.momNotRequiredBy ? `by ${v.momNotRequiredBy}` : null, v.momNotRequiredAt ? formatDateTime(v.momNotRequiredAt) : null]
                                .filter(Boolean)
                                .join(' · ')}
                            </div>
                          </>
                        ) : (
                          <Badge className="bg-amber-100 text-amber-800 ring-1 ring-amber-200">Pending</Badge>
                        )}
                      </td>
                      <td className="px-3 py-2 text-right">
                        {!v.momNotRequired ? (
                          <Button size="sm" onClick={() => navigate(`/visitors/${v.visitId}`)}>Add minutes</Button>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {withoutMomCount > withoutMomItems.length ? (
                <p className="px-3 pt-2 text-xs text-ink-muted">
                  Showing the latest {withoutMomItems.length} of {withoutMomCount}.
                </p>
              ) : null}
            </div>
          )}
        </Panel>
      ) : null}

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
