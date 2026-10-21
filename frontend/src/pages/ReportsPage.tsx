import { useEffect, useState } from 'react'
import { addDays, format, startOfMonth, startOfWeek, subMonths, subYears } from 'date-fns'
import { apiErrorMessage, mastersApi, reportsApi } from '../lib/api'
import type { MasterItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextInput, TextSelect } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { downloadBlob, formatDate, statusBadgeClass, todayIsoDate } from '../lib/utils'
import { LoadingMask } from '../components/LoadingMask'
import { SortHeader } from '../components/ui/SortHeader'
import { nextSort, type SortState } from '../lib/useSort'

/** Matches the JSON shape returned by GET /api/reports/visitors. */
interface ReportRow {
  visitNumber: string
  visitorName: string
  company: string
  host: string
  department: string
  visitDate: string
  status: string
  isManualCheckOut?: boolean
  purposes: string
  locations: string
  feedbackAvg?: string
  feedbackRatings?: string
  feedbackComments?: string
  phone?: string
  checkInAt?: string
  checkOutAt?: string
  duration?: string
  defaulterReason?: string
}

interface ReportResult {
  reportType: string
  generatedAt: string
  generatedBy: string
  total: number
  manualCheckOutCount?: number
  defaulterCount?: number
  allowedVisitMinutes?: number
  rows: ReportRow[]
}

type Duration = 'today' | 'week' | 'month' | '3months' | '6months' | 'year' | 'custom'

const DURATIONS: { value: Duration; label: string }[] = [
  { value: 'today', label: 'Today' },
  { value: 'week', label: 'This week' },
  { value: 'month', label: 'This month' },
  { value: '3months', label: 'Last 3 months' },
  { value: '6months', label: 'Last 6 months' },
  { value: 'year', label: 'Last 1 year' },
  { value: 'custom', label: 'Custom dates' },
]

/** From/To for a preset, ending today. Weeks start on Monday. */
function durationRange(duration: Exclude<Duration, 'custom'>): { from: string; to: string } {
  const today = new Date()
  const iso = (d: Date) => format(d, 'yyyy-MM-dd')
  switch (duration) {
    case 'today':
      return { from: iso(today), to: iso(today) }
    case 'week':
      return { from: iso(startOfWeek(today, { weekStartsOn: 1 })), to: iso(today) }
    case 'month':
      return { from: iso(startOfMonth(today)), to: iso(today) }
    case '3months':
      return { from: iso(addDays(subMonths(today, 3), 1)), to: iso(today) }
    case '6months':
      return { from: iso(addDays(subMonths(today, 6), 1)), to: iso(today) }
    case 'year':
      return { from: iso(addDays(subYears(today, 1), 1)), to: iso(today) }
  }
}

function formatMinutes(minutes: number) {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return h ? `${h}h${m ? ` ${m}m` : ''}` : `${m}m`
}

export function ReportsPage() {
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [dateFrom, setDateFrom] = useState(todayIsoDate())
  const [dateTo, setDateTo] = useState(todayIsoDate())
  const [departmentId, setDepartmentId] = useState('')
  const [locationId, setLocationId] = useState('')
  const [company, setCompany] = useState('')
  const [reportType, setReportType] = useState('daily')
  const [duration, setDuration] = useState<Duration>('today')
  const [rows, setRows] = useState<ReportRow[]>([])
  const [total, setTotal] = useState(0)
  const [manualCheckOutCount, setManualCheckOutCount] = useState(0)
  const [defaulterCount, setDefaulterCount] = useState(0)
  const [allowedMinutes, setAllowedMinutes] = useState<number | null>(null)
  const [resultType, setResultType] = useState('daily')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [exporting, setExporting] = useState<string | null>(null)
  const [hasRun, setHasRun] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [runParams, setRunParams] = useState<ReturnType<typeof params> | null>(null)
  const [sort, setSort] = useState<SortState | null>(null)

  function onSort(key: string) {
    const next = nextSort(sort, key)
    setSort(next)
    if (runParams) {
      const base = { ...runParams, sortBy: next.key, sortDir: next.dir }
      setRunParams(base)
      void fetchPage(base, 1, pageSize)
    }
  }

  const sortProps = { sort, onSort }

  useEffect(() => {
    void mastersApi.departments().then(setDepartments)
    void mastersApi.locations(true).then(setLocations)
  }, [])

  function onReportTypeChange(next: string) {
    setReportType(next)
    // Daily always means "today" — keep the date controls in sync so the run matches the label.
    if (next === 'daily') {
      const today = todayIsoDate()
      setDateFrom(today)
      setDateTo(today)
      setDuration('today')
    }
  }

  function onDurationChange(next: Duration) {
    setDuration(next)
    if (next === 'custom') {
      if (reportType === 'daily') setReportType('range')
      return
    }
    const range = durationRange(next)
    setDateFrom(range.from)
    setDateTo(range.to)
    // "Daily" is today only; any longer period becomes a date-range report.
    if (reportType === 'daily' && next !== 'today') setReportType('range')
  }

  function params() {
    const from = reportType === 'daily' ? todayIsoDate() : dateFrom
    const to = reportType === 'daily' ? todayIsoDate() : dateTo
    return {
      reportType,
      dateFrom: from,
      dateTo: to,
      departmentId: departmentId || undefined,
      locationId: locationId || undefined,
      company: company || undefined,
      sortBy: sort?.key,
      sortDir: sort?.dir,
    }
  }

  async function fetchPage(base: ReturnType<typeof params>, nextPage: number, size: number) {
    setLoading(true)
    setError(null)
    try {
      const data = (await reportsApi.visitorsJson({ ...base, page: nextPage, pageSize: size })) as ReportResult
      setRows(Array.isArray(data?.rows) ? data.rows : [])
      setTotal(data?.total ?? 0)
      setManualCheckOutCount(data?.manualCheckOutCount ?? 0)
      setDefaulterCount(data?.defaulterCount ?? 0)
      setAllowedMinutes(data?.allowedVisitMinutes ?? null)
      setResultType(base.reportType)
      setPage(nextPage)
      setHasRun(true)
    } catch (e) {
      setError(apiErrorMessage(e))
      setRows([])
      setTotal(0)
      setManualCheckOutCount(0)
      setDefaulterCount(0)
    } finally {
      setLoading(false)
    }
  }

  function runReport() {
    const base = params()
    setRunParams(base)
    void fetchPage(base, 1, pageSize)
  }

  function changePageSize(size: number) {
    setPageSize(size)
    if (runParams) void fetchPage(runParams, 1, size)
  }

  async function exportFormat(format: 'excel' | 'pdf' | 'csv') {
    setExporting(format)
    setError(null)
    try {
      const blob = await reportsApi.exportFile(format, params())
      const ext = format === 'excel' ? 'xlsx' : format
      downloadBlob(blob, `tiaano-${reportType === 'defaulters' ? 'checkout-defaulters' : 'visitors'}.${ext}`)
    } catch (e) {
      setError(apiErrorMessage(e, 'Export failed'))
    } finally {
      setExporting(null)
    }
  }

  return (
    <div>
      {loading ? <Spinner label="Running report…" /> : null}
      {exporting ? <LoadingMask label={`Exporting ${exporting.toUpperCase()}…`} /> : null}
      <PageHeader title="Reports" subtitle="Visitor activity reports with export" />

      <Panel className="mb-4">
        <div className="grid gap-3 md:grid-cols-4 xl:grid-cols-7">
          <div>
            <FieldLabel>Report type</FieldLabel>
            <TextSelect value={reportType} onChange={(e) => onReportTypeChange(e.target.value)}>
              <option value="daily">Daily</option>
              <option value="range">Date range</option>
              <option value="department">By department</option>
              <option value="manualcheckout">Manual check-outs</option>
              <option value="defaulters">Check-out defaulters</option>
            </TextSelect>
          </div>
          <div>
            <FieldLabel>Duration</FieldLabel>
            <TextSelect value={duration} onChange={(e) => onDurationChange(e.target.value as Duration)}>
              {DURATIONS.map((d) => (
                <option key={d.value} value={d.value}>{d.label}</option>
              ))}
            </TextSelect>
          </div>
          <div>
            <FieldLabel>From</FieldLabel>
            <TextInput
              type="date"
              value={dateFrom}
              max={dateTo || undefined}
              disabled={reportType === 'daily'}
              onChange={(e) => {
                setDateFrom(e.target.value)
                setDuration('custom')
              }}
            />
          </div>
          <div>
            <FieldLabel>To</FieldLabel>
            <TextInput
              type="date"
              value={dateTo}
              min={dateFrom || undefined}
              disabled={reportType === 'daily'}
              onChange={(e) => {
                setDateTo(e.target.value)
                setDuration('custom')
              }}
            />
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
            <FieldLabel>Location</FieldLabel>
            <TextSelect value={locationId} onChange={(e) => setLocationId(e.target.value)}>
              <option value="">All</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </TextSelect>
          </div>
          <div>
            <FieldLabel>Company</FieldLabel>
            <TextInput value={company} onChange={(e) => setCompany(e.target.value)} />
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-2">
          <Button onClick={runReport}>Run report</Button>
          <Button variant="secondary" disabled={!!exporting} onClick={() => void exportFormat('excel')}>
            {exporting === 'excel' ? 'Exporting…' : 'Export Excel'}
          </Button>
          <Button variant="secondary" disabled={!!exporting} onClick={() => void exportFormat('pdf')}>
            {exporting === 'pdf' ? 'Exporting…' : 'Export PDF'}
          </Button>
          <Button variant="secondary" disabled={!!exporting} onClick={() => void exportFormat('csv')}>
            {exporting === 'csv' ? 'Exporting…' : 'Export CSV'}
          </Button>
        </div>
      </Panel>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {!loading && hasRun && rows.length === 0 ? (
        <EmptyState
          title={resultType === 'defaulters' ? 'No check-out defaulters in this period' : 'No visitors matched this report'}
          description="Try a wider date range or clear the filters."
        />
      ) : null}
      {!loading && !hasRun ? <EmptyState title="Run a report to see results" /> : null}

      {!loading && rows.length > 0 && resultType === 'defaulters' ? (
        <Panel className="overflow-x-auto p-0">
          <div className="border-b border-gray-100 px-4 py-3 text-sm text-ink-muted">
            <span className="font-medium text-red-600">
              {total} check-out defaulter{total === 1 ? '' : 's'}
            </span>
            {allowedMinutes ? <> · Allowed visit duration {formatMinutes(allowedMinutes)} (Settings → Max visit duration)</> : null}
          </div>
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr>
                <SortHeader sortKey="visitnumber" {...sortProps}>Visit #</SortHeader>
                <SortHeader sortKey="visitor" {...sortProps}>Visitor</SortHeader>
                <SortHeader sortKey="phone" {...sortProps}>Phone</SortHeader>
                <SortHeader sortKey="host" {...sortProps}>Person to meet</SortHeader>
                <SortHeader sortKey="department" {...sortProps}>Department</SortHeader>
                <SortHeader sortKey="location" {...sortProps}>Location</SortHeader>
                <SortHeader sortKey="checkin" {...sortProps}>Check-in</SortHeader>
                <SortHeader sortKey="checkout" {...sortProps}>Check-out</SortHeader>
                <th className="px-4 py-3">Duration</th>
                <SortHeader sortKey="issue" {...sortProps}>Check-out issue</SortHeader>
              </tr>
            </thead>
            <tbody>
              {rows.map((v) => (
                <tr key={v.visitNumber} className="border-t border-gray-100">
                  <td className="px-4 py-3 font-mono text-xs">{v.visitNumber}</td>
                  <td className="px-4 py-3 font-medium">
                    {v.visitorName}
                    {v.company ? <div className="text-xs text-ink-muted">{v.company}</div> : null}
                  </td>
                  <td className="px-4 py-3 whitespace-nowrap">{v.phone || '—'}</td>
                  <td className="px-4 py-3">{v.host}</td>
                  <td className="px-4 py-3">{v.department}</td>
                  <td className="px-4 py-3">{v.locations || '—'}</td>
                  <td className="px-4 py-3 whitespace-nowrap">{v.checkInAt || '—'}</td>
                  <td className="px-4 py-3 whitespace-nowrap">
                    {v.checkOutAt || <Badge className="bg-red-100 text-red-700">Still inside</Badge>}
                  </td>
                  <td className="px-4 py-3 whitespace-nowrap">{v.duration || '—'}</td>
                  <td className="px-4 py-3 text-red-700">{v.defaulterReason}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={(p) => runParams && void fetchPage(runParams, p, pageSize)}
            onPageSizeChange={changePageSize}
          />
        </Panel>
      ) : null}

      {!loading && rows.length > 0 && resultType !== 'defaulters' ? (
        <Panel className="overflow-x-auto p-0">
          <div className="border-b border-gray-100 px-4 py-3 text-sm text-ink-muted">
            {total} visitor{total === 1 ? '' : 's'}
            {' · '}
            <span className="font-medium text-ink">{manualCheckOutCount} manual check-out{manualCheckOutCount === 1 ? '' : 's'}</span>
            {' · '}
            <span className="font-medium text-red-600">{defaulterCount} check-out defaulter{defaulterCount === 1 ? '' : 's'}</span>
          </div>
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr>
                <SortHeader sortKey="visitnumber" {...sortProps}>Visit #</SortHeader>
                <SortHeader sortKey="visitor" {...sortProps}>Visitor</SortHeader>
                <SortHeader sortKey="company" {...sortProps}>Company</SortHeader>
                <SortHeader sortKey="host" {...sortProps}>Person to meet</SortHeader>
                <SortHeader sortKey="department" {...sortProps}>Department</SortHeader>
                <SortHeader sortKey="date" {...sortProps}>Date</SortHeader>
                <SortHeader sortKey="status" {...sortProps}>Status</SortHeader>
                <SortHeader sortKey="manual" {...sortProps}>Manual check-out</SortHeader>
                <th className="px-4 py-3">Purpose</th>
                <SortHeader sortKey="location" {...sortProps}>Location</SortHeader>
                <SortHeader sortKey="feedback" {...sortProps}>Feedback</SortHeader>
                <th className="px-4 py-3">Ratings</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((v) => (
                <tr key={v.visitNumber} className="border-t border-gray-100">
                  <td className="px-4 py-3 font-mono text-xs">{v.visitNumber}</td>
                  <td className="px-4 py-3 font-medium">{v.visitorName}</td>
                  <td className="px-4 py-3">{v.company}</td>
                  <td className="px-4 py-3">{v.host}</td>
                  <td className="px-4 py-3">{v.department}</td>
                  <td className="px-4 py-3">{formatDate(v.visitDate)}</td>
                  <td className="px-4 py-3">
                    <Badge className={statusBadgeClass(v.status)}>{v.status}</Badge>
                  </td>
                  <td className="px-4 py-3">{v.isManualCheckOut ? 'Yes' : 'No'}</td>
                  <td className="px-4 py-3">{v.purposes || '—'}</td>
                  <td className="px-4 py-3">{v.locations || '—'}</td>
                  <td className="px-4 py-3 whitespace-nowrap text-amber-600 font-medium">
                    {v.feedbackAvg ? `${v.feedbackAvg} ★` : '—'}
                  </td>
                  <td className="px-4 py-3 max-w-xs text-xs text-ink-muted" title={v.feedbackRatings || undefined}>
                    {v.feedbackRatings || '—'}
                    {v.feedbackComments ? (
                      <div className="mt-1 italic text-ink">{v.feedbackComments}</div>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={(p) => runParams && void fetchPage(runParams, p, pageSize)}
            onPageSizeChange={changePageSize}
          />
        </Panel>
      ) : null}
    </div>
  )
}
