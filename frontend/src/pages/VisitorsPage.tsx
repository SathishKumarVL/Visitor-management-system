import { useCallback, useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, visitorsApi } from '../lib/api'
import type { MasterItemDto, VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { formatDate, statusBadgeClass } from '../lib/utils'

export function VisitorsPage() {
  const [params, setParams] = useSearchParams()
  const [items, setItems] = useState<VisitorListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(Number(params.get('page') || 1))
  const [pageSize] = useState(20)
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

  useEffect(() => {
    void mastersApi.departments().then(setDepartments).catch(() => undefined)
  }, [])

  const load = useCallback(async () => {
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
        page,
        pageSize,
      })
      setItems(result.items)
      setTotal(result.totalCount)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [query, company, host, departmentId, status, dateFrom, dateTo, quickFilter, page, pageSize])

  useEffect(() => {
    void load()
  }, [load])

  const totalPages = Math.max(1, Math.ceil(total / pageSize))

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
              <option value="today">Today</option>
              <option value="approved">Approved</option>
              <option value="inside">Inside</option>
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
              setPage(1)
            }}
          >
            Reset
          </Button>
        </div>
      </Panel>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {loading ? <Spinner /> : null}
      {!loading && items.length === 0 ? <EmptyState title="No visitors found" /> : null}

      {!loading && items.length > 0 ? (
        <Panel className="overflow-x-auto p-0">
          <table className="min-w-full text-left text-sm">
            <thead className="border-b border-gray-200 bg-gray-50 text-gray-500">
              <tr>
                <th className="px-4 py-3 font-medium">Visitor</th>
                <th className="px-4 py-3 font-medium">Company</th>
                <th className="px-4 py-3 font-medium">Person to meet</th>
                <th className="px-4 py-3 font-medium">Date</th>
                <th className="px-4 py-3 font-medium">Status</th>
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
                  <td className="px-4 py-3">
                    <Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge>
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
          <div className="flex items-center justify-between gap-3 px-4 py-3">
            <span className="text-sm text-gray-500">
              Page {page} of {totalPages}
            </span>
            <div className="flex gap-2">
              <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                Previous
              </Button>
              <Button variant="secondary" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Next
              </Button>
            </div>
          </div>
        </Panel>
      ) : null}
    </div>
  )
}
