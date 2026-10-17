import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, visitorsApi } from '../lib/api'
import type { VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { TextInput } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { cn, formatDateTime, formatDuration } from '../lib/utils'

const PERIODS = [
  { value: 'today', label: 'Today', quickFilter: 'manualcheckouttoday' },
  { value: 'all', label: 'All', quickFilter: 'manualcheckout' },
] as const

type Period = (typeof PERIODS)[number]['value']

/** Visitors checked out by an admin without the visitor's face or feedback, or by the automatic check-out. */
export function ManualCheckoutsPage() {
  const [params, setParams] = useSearchParams()
  const period: Period = params.get('period') === 'all' ? 'all' : 'today'
  const [query, setQuery] = useState('')
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const requestSeq = useRef(0)

  const load = useCallback(async () => {
    const seq = ++requestSeq.current
    setLoading(true)
    setError(null)
    try {
      const result = await visitorsApi.search({
        quickFilter: PERIODS.find((p) => p.value === period)!.quickFilter,
        query: query.trim() || undefined,
        sortBy: 'checkout',
        sortDir: 'desc',
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
  }, [period, query, page, pageSize])

  useEffect(() => {
    void load()
  }, [load])

  return (
    <div>
      <PageHeader
        eyebrow="Dashboard"
        title="Manual check-outs"
        subtitle="Visitors checked out by an admin without their face or feedback, or automatically after the maximum stay."
        actions={
          <Link to="/dashboard" className="text-sm font-semibold text-primary hover:underline">
            ← Back to dashboard
          </Link>
        }
      />

      <Panel className="mb-4 flex flex-wrap items-center gap-3">
        <div className="inline-flex rounded-xl bg-gray-100 p-1" role="tablist">
          {PERIODS.map((p) => (
            <button
              key={p.value}
              type="button"
              role="tab"
              aria-selected={period === p.value}
              onClick={() => {
                setPage(1)
                setParams((prev) => {
                  const next = new URLSearchParams(prev)
                  next.set('period', p.value)
                  return next
                })
              }}
              className={cn(
                'rounded-lg px-4 py-1.5 text-sm font-medium transition',
                period === p.value ? 'bg-white text-ink shadow-sm' : 'text-ink-muted hover:text-ink',
              )}
            >
              {p.label}
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
        <span className="ml-auto text-sm text-ink-muted">
          {total} {total === 1 ? 'visitor' : 'visitors'}
        </span>
      </Panel>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {loading ? <Spinner label="Loading manual check-outs…" /> : null}
      {!loading && items.length === 0 ? (
        <EmptyState
          title={period === 'today' ? 'No manual check-outs today' : 'No manual check-outs'}
          description="Every visitor checked out at the desk with their face."
        />
      ) : null}

      {!loading && items.length > 0 ? (
        <Panel className="overflow-x-auto p-0">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr className="whitespace-nowrap">
                <th className="px-4 py-3 font-medium">Visitor</th>
                <th className="px-4 py-3 font-medium">Person to meet</th>
                <th className="px-4 py-3 font-medium">Checked in</th>
                <th className="px-4 py-3 font-medium">Checked out</th>
                <th className="px-4 py-3 font-medium">Checked out by</th>
                <th className="px-4 py-3 font-medium">MoM</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {items.map((v) => (
                <tr key={v.visitId} className="border-t border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3">
                    <div className="font-medium text-ink">{v.visitorName}</div>
                    <div className="text-xs text-ink-muted">{[v.companyName, v.visitNumber].filter(Boolean).join(' · ')}</div>
                  </td>
                  <td className="px-4 py-3">
                    <div>{v.hostName}</div>
                    <div className="text-xs text-ink-muted">{v.departmentName}</div>
                  </td>
                  <td className="px-4 py-3 whitespace-nowrap">{v.checkInAt ? formatDateTime(v.checkInAt) : '—'}</td>
                  <td className="px-4 py-3 whitespace-nowrap">
                    <div>{v.checkOutAt ? formatDateTime(v.checkOutAt) : '—'}</div>
                    {v.durationMinutes != null ? (
                      <div className="text-xs text-ink-muted">Stayed {formatDuration(v.durationMinutes)}</div>
                    ) : null}
                  </td>
                  <td className="px-4 py-3">
                    {v.checkedOutBy ? (
                      v.checkedOutBy
                    ) : (
                      <Badge className="bg-orange-100 text-orange-900 ring-1 ring-orange-200">Automatic</Badge>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <MomBadge visit={v} />
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Link to={`/visitors/${v.visitId}`} className="font-medium text-steel hover:underline">Open</Link>
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

export function MomBadge({ visit }: { visit: VisitorListItemDto }) {
  if (visit.momEmailSent) return <Badge className="bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200">Sent</Badge>
  if (visit.momNotRequired) return <Badge className="bg-slate-100 text-slate-700 ring-1 ring-slate-200">Not required</Badge>
  if (visit.hasMeetingMinutes) return <Badge className="bg-sky-50 text-sky-800 ring-1 ring-sky-200">Saved, not sent</Badge>
  return <Badge className="bg-amber-100 text-amber-800 ring-1 ring-amber-200">Not recorded</Badge>
}
