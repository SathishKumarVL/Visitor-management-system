import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, dashboardApi, visitorsApi } from '../lib/api'
import type { MomNotSentByEmployeeDto, VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { cn, formatDateTime, hasAnyRole } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { MomBadge } from './ManualCheckoutsPage'

const TABS = [
  { value: 'pending', label: 'Pending', quickFilter: 'momnotsent' },
  { value: 'notrequired', label: 'Not required', quickFilter: 'momnotrequired' },
] as const

type Tab = (typeof TABS)[number]['value']

/** Checked-out visitors whose minutes-of-meeting email has not gone out, per employee and per visit. */
export function MomPendingPage() {
  const navigate = useNavigate()
  const roles = useAuthStore((s) => s.user?.roles ?? [])
  const canEditMom = hasAnyRole(roles, ['SuperAdmin', 'Admin', 'Host'])
  const seesEveryone = hasAnyRole(roles, ['SuperAdmin', 'Admin'])
  const [params, setParams] = useSearchParams()
  const tab: Tab = params.get('tab') === 'notrequired' ? 'notrequired' : 'pending'
  const host = params.get('host') || ''

  const [summary, setSummary] = useState<MomNotSentByEmployeeDto[]>([])
  const [query, setQuery] = useState('')
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const requestSeq = useRef(0)

  function setParam(key: string, value: string | null) {
    setPage(1)
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (value) next.set(key, value)
      else next.delete(key)
      return next
    })
  }

  useEffect(() => {
    if (!seesEveryone) return
    dashboardApi.momNotSentByEmployee().then(setSummary).catch(() => setSummary([]))
  }, [seesEveryone])

  const load = useCallback(async () => {
    const seq = ++requestSeq.current
    setLoading(true)
    setError(null)
    try {
      const result = await visitorsApi.search({
        quickFilter: TABS.find((t) => t.value === tab)!.quickFilter,
        host: host || undefined,
        query: query.trim() || undefined,
        sortBy: 'checkout',
        sortDir: 'asc',
        page,
        pageSize,
      })
      if (seq !== requestSeq.current) return
      setItems(result.items)
      setTotal(result.totalCount)
    } catch (e) {
      if (seq === requestSeq.current) setError(apiErrorMessage(e))
    } finally {
      if (seq === requestSeq.current) setLoading(false)
    }
  }, [tab, host, query, page, pageSize])

  useEffect(() => {
    void load()
  }, [load])

  const pendingTotal = summary.reduce((sum, e) => sum + e.notRecorded + e.notSent, 0)
  const notRequiredTotal = summary.reduce((sum, e) => sum + e.notRequired, 0)

  return (
    <div>
      <PageHeader
        eyebrow="Dashboard"
        title="Checked out, MOM not sent"
        subtitle={
          seesEveryone
            ? 'Visitors who have left without the minutes-of-meeting email, by employee and by visit.'
            : 'Your visitors who have left without the minutes-of-meeting email.'
        }
        actions={
          <Link to="/dashboard" className="text-sm font-semibold text-primary hover:underline">
            ← Back to dashboard
          </Link>
        }
      />

      {seesEveryone && summary.length > 0 ? (
        <Panel className="mb-4 overflow-x-auto p-0">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-gray-100 px-4 py-3">
            <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">By employee</h2>
            <div className="flex flex-wrap gap-2">
              <Badge className="bg-amber-100 text-amber-800 ring-1 ring-amber-200">{pendingTotal} pending</Badge>
              <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">{notRequiredTotal} not required</Badge>
            </div>
          </div>
          <table className="min-w-full text-left text-sm">
            <thead className="text-xs uppercase tracking-wide text-ink-muted">
              <tr className="whitespace-nowrap">
                <th className="px-4 py-2">Employee</th>
                <th className="px-4 py-2 text-right">Not recorded</th>
                <th className="px-4 py-2 text-right">Saved, not sent</th>
                <th className="px-4 py-2 text-right">Not required</th>
                <th className="px-4 py-2">Oldest pending</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody>
              {summary.map((e) => {
                const selected = host === e.employeeName
                return (
                  <tr key={e.employeeId} className={cn('border-t border-gray-100', selected && 'bg-mint')}>
                    <td className="px-4 py-2">
                      <div className="font-medium text-ink">{e.employeeName}</div>
                      <div className="text-xs text-ink-muted">{e.departmentName || '—'}</div>
                    </td>
                    <td className="px-4 py-2 text-right tabular-nums">{e.notRecorded}</td>
                    <td className="px-4 py-2 text-right tabular-nums">{e.notSent}</td>
                    <td className="px-4 py-2 text-right tabular-nums text-ink-muted">{e.notRequired}</td>
                    <td className="px-4 py-2 whitespace-nowrap">{e.oldestCheckOutAt ? formatDateTime(e.oldestCheckOutAt) : '—'}</td>
                    <td className="px-4 py-2 text-right">
                      <Button size="sm" variant="secondary" onClick={() => setParam('host', selected ? null : e.employeeName)}>
                        {selected ? 'Show all' : 'Show visits'}
                      </Button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </Panel>
      ) : null}

      <Panel className="mb-4 flex flex-wrap items-center gap-3">
        <div className="inline-flex rounded-xl bg-gray-100 p-1" role="tablist">
          {TABS.map((t) => (
            <button
              key={t.value}
              type="button"
              role="tab"
              aria-selected={tab === t.value}
              onClick={() => setParam('tab', t.value === 'pending' ? null : t.value)}
              className={cn(
                'rounded-lg px-4 py-1.5 text-sm font-medium transition',
                tab === t.value ? 'bg-white text-ink shadow-sm' : 'text-ink-muted hover:text-ink',
              )}
            >
              {t.label}
            </button>
          ))}
        </div>
        <TextInput
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setPage(1)
          }}
          placeholder="Search visitor, phone, visit number"
          className="w-full sm:w-72"
        />
        {host ? (
          <span className="inline-flex items-center gap-2 rounded-full bg-mint px-3 py-1 text-sm text-ink">
            Person to meet: <strong>{host}</strong>
            <button type="button" className="text-ink-muted hover:text-ink" aria-label="Clear employee filter" onClick={() => setParam('host', null)}>
              ✕
            </button>
          </span>
        ) : null}
        <span className="ml-auto text-sm text-ink-muted">
          {total} {total === 1 ? 'visit' : 'visits'}
        </span>
      </Panel>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {loading ? <Spinner label="Loading visits…" /> : null}
      {!loading && items.length === 0 ? (
        <EmptyState
          title={tab === 'pending' ? 'No minutes pending' : 'Nothing marked as not required'}
          description={tab === 'pending' ? 'The MOM email has been sent for every checked-out visit.' : undefined}
        />
      ) : null}

      {!loading && items.length > 0 ? (
        <Panel className="overflow-x-auto p-0">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr className="whitespace-nowrap">
                <th className="px-4 py-3 font-medium">Visitor</th>
                <th className="px-4 py-3 font-medium">Person to meet</th>
                <th className="px-4 py-3 font-medium">Checked out</th>
                <th className="px-4 py-3 font-medium">MoM</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {items.map((v) => (
                <tr key={v.visitId} className="border-t border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3">
                    <Link to={`/visitors/${v.visitId}`} className="font-medium text-ink hover:text-primary">{v.visitorName}</Link>
                    <div className="text-xs text-ink-muted">{[v.companyName, v.visitNumber].filter(Boolean).join(' · ')}</div>
                  </td>
                  <td className="px-4 py-3">
                    <div>{v.hostName}</div>
                    <div className="text-xs text-ink-muted">{v.departmentName}</div>
                  </td>
                  <td className="px-4 py-3 whitespace-nowrap">{v.checkOutAt ? formatDateTime(v.checkOutAt) : '—'}</td>
                  <td className="px-4 py-3">
                    <MomBadge visit={v} />
                    {v.momNotRequired ? (
                      <div className="mt-0.5 text-xs text-ink-muted">
                        {[v.momNotRequiredBy ? `by ${v.momNotRequiredBy}` : null, v.momNotRequiredAt ? formatDateTime(v.momNotRequiredAt) : null]
                          .filter(Boolean)
                          .join(' · ')}
                      </div>
                    ) : null}
                  </td>
                  <td className="px-4 py-3 text-right whitespace-nowrap">
                    {canEditMom && !v.momNotRequired ? (
                      <Button size="sm" onClick={() => navigate(`/visitors/${v.visitId}/minutes`)}>
                        {v.hasMeetingMinutes ? 'Send MOM' : 'Add minutes'}
                      </Button>
                    ) : (
                      <Link to={`/visitors/${v.visitId}`} className="font-medium text-steel hover:underline">Open</Link>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size)
              setPage(1)
            }}
          />
        </Panel>
      ) : null}
    </div>
  )
}
