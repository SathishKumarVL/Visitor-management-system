import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, visitorsApi } from '../lib/api'
import type { MasterItemDto, VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { SortHeader } from '../components/ui/SortHeader'
import { nextSort, type SortState } from '../lib/useSort'
import { formatDate, formatDateTime, statusBadgeClass } from '../lib/utils'

const QUICK_FILTERS = [
  { value: 'today', label: 'Today' },
  { value: 'yesterday', label: 'Yesterday' },
  { value: 'thisweek', label: 'This week' },
  { value: 'thismonth', label: 'This month' },
  { value: 'approved', label: 'Approved' },
  { value: 'inside', label: 'Inside' },
  { value: 'checkedouttoday', label: 'Checked out today' },
  { value: 'manualcheckouttoday', label: 'Manual check-outs today' },
  { value: 'manualcheckout', label: 'All manual check-outs' },
]

export function VisitorsPage() {
  const [params, setParams] = useSearchParams()
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(Number(params.get('page') || 1))
  const [pageSize, setPageSize] = useState(20)
  const requestSeq = useRef(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [departments, setDepartments] = useState<MasterItemDto[]>([])

  const [query, setQuery] = useState(params.get('query') || '')
  const [company, setCompany] = useState('')
  const [host, setHost] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [status, setStatus] = useState(params.get('status') || '')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [quickFilter, setQuickFilter] = useState(params.get('quickFilter') || '')
  const [sort, setSort] = useState<SortState | null>(null)
  const showCheckOut = quickFilter.includes('checkout')

  // The dashboard links here with ?quickFilter=…; follow later link clicks too, not just the first mount.
  const urlQuickFilter = params.get('quickFilter') || ''
  const [lastUrlQuickFilter, setLastUrlQuickFilter] = useState(urlQuickFilter)
  if (urlQuickFilter !== lastUrlQuickFilter) {
    setLastUrlQuickFilter(urlQuickFilter)
    setQuickFilter(urlQuickFilter)
  }

  useEffect(() => {
    void mastersApi.departments().then(setDepartments).catch(() => undefined)
  }, [])

  const filterKey = JSON.stringify([query, company, host, departmentId, status, dateFrom, dateTo, quickFilter, sort])
  const lastFilterKey = useRef(filterKey)
  useEffect(() => {
    if (lastFilterKey.current === filterKey) return
    lastFilterKey.current = filterKey
    setPage(1)
  }, [filterKey])

  const load = useCallback(async () => {
    const seq = ++requestSeq.current
    setLoading(true)
    setError(null)
    try {
      const result = await visitorsApi.search({
        query: query || undefined,
        company: company || undefined,
        host: host || undefined,
        departmentId: departmentId || undefined,
        status: status || undefined,
        dateFrom: dateFrom || undefined,
        dateTo: dateTo || undefined,
        quickFilter: quickFilter || undefined,
        sortBy: sort?.key,
        sortDir: sort?.dir,
        page,
        pageSize,
      })
      if (seq !== requestSeq.current) return
      setItems(result.items)
      setTotal(result.totalCount)
    } catch (e) {
      if (seq !== requestSeq.current) return
      setError(apiErrorMessage(e))
    } finally {
      if (seq === requestSeq.current) setLoading(false)
    }
  }, [query, company, host, departmentId, status, dateFrom, dateTo, quickFilter, sort, page, pageSize])

  const onSort = (key: string) => setSort((current) => nextSort(current, key))

  useEffect(() => {
    void load()
  }, [load])

  return (
    <div>
      <PageHeader
        title="Visitors"
        subtitle={`${total} record${total === 1 ? '' : 's'}`}
        actions={
          <Link to="/visitors/new">
            <Button variant="amber">New visitor</Button>
          </Link>
        }
      />

      <Panel className="mb-4">
        <div className="grid gap-3 md:grid-cols-3 xl:grid-cols-4">
          <div>
            <FieldLabel>Search</FieldLabel>
            <TextInput value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Name, phone, number…" />
          </div>
          <div>
            <FieldLabel>Company</FieldLabel>
            <TextInput value={company} onChange={(e) => setCompany(e.target.value)} />
          </div>
          <div>
            <FieldLabel>Person to meet</FieldLabel>
            <TextInput value={host} onChange={(e) => setHost(e.target.value)} />
          </div>
          <div>
            <FieldLabel>Department</FieldLabel>
            <TextSelect value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
              <option value="">All</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>{d.name}</option>
              ))}
            </TextSelect>
          </div>
          <div>
            <FieldLabel>Status</FieldLabel>
            <TextSelect value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">All</option>
              <option value="0">Expected</option>
              <option value="1">PendingApproval</option>
              <option value="2">Approved</option>
              <option value="3">Rejected</option>
              <option value="5">Inside</option>
              <option value="6">CheckedOut</option>
            </TextSelect>
          </div>
          <div>
            <FieldLabel>From</FieldLabel>
            <TextInput type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} />
          </div>
          <div>
            <FieldLabel>To</FieldLabel>
            <TextInput type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
          </div>
          <div>
            <FieldLabel>Quick filter</FieldLabel>
            <TextSelect
              value={quickFilter}
              onChange={(e) => {
                setQuickFilter(e.target.value)
                setParams((p) => {
                  const next = new URLSearchParams(p)
                  if (e.target.value) next.set('quickFilter', e.target.value)
                  else next.delete('quickFilter')
                  return next
                })
              }}
            >
              <option value="">None</option>
              {QUICK_FILTERS.map((f) => (
                <option key={f.value} value={f.value}>{f.label}</option>
              ))}
            </TextSelect>
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-2">
          <Button
            onClick={() => {
              setPage(1)
              void load()
            }}
          >
            Apply filters
          </Button>
          <Button
            variant="secondary"
            onClick={() => {
              setQuery('')
              setCompany('')
              setHost('')
              setDepartmentId('')
              setStatus('')
              setDateFrom('')
              setDateTo('')
              setQuickFilter('')
              setSort(null)
              setParams((p) => {
                const next = new URLSearchParams(p)
                next.delete('quickFilter')
                return next
              })
              setPage(1)
            }}
          >
            Reset
          </Button>
        </div>
      </Panel>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {loading ? <Spinner label="Loading visitors…" /> : null}
      {!loading && items.length === 0 ? <EmptyState title="No visitors found" /> : null}

      {!loading && items.length > 0 ? (
        <Panel className="overflow-x-auto p-0">
          <table className="min-w-full text-left text-sm">
            <thead className="border-b border-gray-200 bg-gray-50 text-gray-500">
              <tr>
                <SortHeader sortKey="visitor" sort={sort} onSort={onSort}>Visitor</SortHeader>
                <SortHeader sortKey="company" sort={sort} onSort={onSort}>Company</SortHeader>
                <SortHeader sortKey="host" sort={sort} onSort={onSort}>Person to meet</SortHeader>
                <SortHeader sortKey="date" sort={sort} onSort={onSort}>Date</SortHeader>
                {showCheckOut ? (
                  <SortHeader sortKey="checkout" sort={sort} onSort={onSort}>Checked out</SortHeader>
                ) : null}
                <SortHeader sortKey="status" sort={sort} onSort={onSort}>Status</SortHeader>
                <th className="px-4 py-3 font-medium" />
              </tr>
            </thead>
            <tbody>
              {items.map((v) => (
                <tr key={v.visitId} className="border-b border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3">
                    <div className="font-medium text-gray-900">{v.visitorName}</div>
                    <div className="text-xs text-gray-500">{v.visitNumber}</div>
                  </td>
                  <td className="px-4 py-3">{v.companyName}</td>
                  <td className="px-4 py-3">
                    <div>{v.hostName}</div>
                    <div className="text-xs text-gray-500">{v.departmentName}</div>
                  </td>
                  <td className="px-4 py-3">{formatDate(v.visitDate)}</td>
                  {showCheckOut ? (
                    <td className="px-4 py-3">{v.checkOutAt ? formatDateTime(v.checkOutAt) : '—'}</td>
                  ) : null}
                  <td className="px-4 py-3">
                    <Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge>
                    {v.isManualCheckOut ? (
                      <Badge className="ml-1 bg-orange-100 text-orange-900 ring-1 ring-orange-200">Manual</Badge>
                    ) : null}
                    {v.statusLabel === 'CheckedOut' && !v.hasMeetingMinutes ? (
                      v.momNotRequired ? (
                        <Badge className="ml-1 bg-slate-100 text-slate-700 ring-1 ring-slate-200">MoM not required</Badge>
                      ) : (
                        <Badge className="ml-1 bg-amber-100 text-amber-900 ring-1 ring-amber-200">MoM pending</Badge>
                      )
                    ) : null}
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Link to={`/visitors/${v.visitId}`} className="font-medium text-steel hover:underline">
                      Open
                    </Link>
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
