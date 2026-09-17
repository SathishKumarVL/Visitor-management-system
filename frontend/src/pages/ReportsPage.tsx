import { useEffect, useState } from 'react'
import { apiErrorMessage, mastersApi, reportsApi } from '../lib/api'
import type { MasterItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextInput, TextSelect } from '../components/ui/Field'
import { downloadBlob, formatDate, statusBadgeClass, todayIsoDate } from '../lib/utils'

/** Matches the JSON shape returned by GET /api/reports/visitors. */
interface ReportRow {
  visitNumber: string
  visitorName: string
  company: string
  host: string
  department: string
  visitDate: string
  status: string
  purposes: string
  locations: string
}

interface ReportResult {
  reportType: string
  generatedAt: string
  generatedBy: string
  total: number
  rows: ReportRow[]
}

export function ReportsPage() {
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [dateFrom, setDateFrom] = useState(todayIsoDate())
  const [dateTo, setDateTo] = useState(todayIsoDate())
  const [departmentId, setDepartmentId] = useState('')
  const [company, setCompany] = useState('')
  const [reportType, setReportType] = useState('daily')
  const [rows, setRows] = useState<ReportRow[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [exporting, setExporting] = useState<string | null>(null)
  const [hasRun, setHasRun] = useState(false)

  useEffect(() => {
    void mastersApi.departments().then(setDepartments)
  }, [])

  function onReportTypeChange(next: string) {
    setReportType(next)
    // Daily always means "today" — keep the date controls in sync so the run matches the label.
    if (next === 'daily') {
      const today = todayIsoDate()
      setDateFrom(today)
      setDateTo(today)
    }
  }

  function params() {
    const from = reportType === 'daily' ? todayIsoDate() : dateFrom
    const to = reportType === 'daily' ? todayIsoDate() : dateTo
    return {
      reportType,
      dateFrom: from,
      dateTo: to,
      departmentId: departmentId || undefined,
      company: company || undefined,
    }
  }

  async function runReport() {
    setLoading(true)
    setError(null)
    try {
      const data = (await reportsApi.visitorsJson(params())) as ReportResult
      setRows(Array.isArray(data?.rows) ? data.rows : [])
      setTotal(data?.total ?? 0)
      setHasRun(true)
    } catch (e) {
      setError(apiErrorMessage(e))
      setRows([])
      setTotal(0)
    } finally {
      setLoading(false)
    }
  }

  async function exportFormat(format: 'excel' | 'pdf' | 'csv') {
    setExporting(format)
    setError(null)
    try {
      const blob = await reportsApi.exportFile(format, params())
      const ext = format === 'excel' ? 'xlsx' : format
      downloadBlob(blob, `tiaano-visitors.${ext}`)
    } catch (e) {
      setError(apiErrorMessage(e, 'Export failed'))
    } finally {
      setExporting(null)
    }
  }

  return (
    <div>
      <PageHeader title="Reports" subtitle="Visitor activity reports with export" />

      <Panel className="mb-4">
        <div className="grid gap-3 md:grid-cols-3 xl:grid-cols-5">
          <div>
            <FieldLabel>Report type</FieldLabel>
            <TextSelect value={reportType} onChange={(e) => onReportTypeChange(e.target.value)}>
              <option value="daily">Daily</option>
              <option value="range">Date range</option>
              <option value="department">By department</option>
            </TextSelect>
          </div>
          <div>
            <FieldLabel>From</FieldLabel>
            <TextInput
              type="date"
              value={dateFrom}
              disabled={reportType === 'daily'}
              onChange={(e) => setDateFrom(e.target.value)}
            />
          </div>
          <div>
            <FieldLabel>To</FieldLabel>
            <TextInput
              type="date"
              value={dateTo}
              disabled={reportType === 'daily'}
              onChange={(e) => setDateTo(e.target.value)}
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
            <FieldLabel>Company</FieldLabel>
            <TextInput value={company} onChange={(e) => setCompany(e.target.value)} />
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-2">
          <Button onClick={() => void runReport()}>Run report</Button>
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
      {loading ? <Spinner /> : null}
      {!loading && hasRun && rows.length === 0 ? (
        <EmptyState title="No visitors matched this report" description="Try a wider date range or clear the filters." />
      ) : null}
      {!loading && !hasRun ? <EmptyState title="Run a report to see results" /> : null}

      {!loading && rows.length > 0 ? (
        <Panel className="overflow-x-auto p-0">
          <div className="border-b border-gray-100 px-4 py-3 text-sm text-ink-muted">
            {total} visitor{total === 1 ? '' : 's'}
          </div>
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr>
                <th className="px-4 py-3">Visit #</th>
                <th className="px-4 py-3">Visitor</th>
                <th className="px-4 py-3">Company</th>
                <th className="px-4 py-3">Host</th>
                <th className="px-4 py-3">Department</th>
                <th className="px-4 py-3">Date</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Purpose</th>
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
                  <td className="px-4 py-3">{v.purposes || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      ) : null}
    </div>
  )
}
