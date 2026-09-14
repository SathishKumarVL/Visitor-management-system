import { useEffect, useState } from 'react'
import { apiErrorMessage, mastersApi, reportsApi } from '../lib/api'
import type { MasterItemDto, VisitorListItemDto } from '../types/api'
import { Alert, Badge, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextInput, TextSelect } from '../components/ui/Field'
import { downloadBlob, formatDate, statusBadgeClass, todayIsoDate } from '../lib/utils'
import { getStoredToken } from '../lib/api'

export function ReportsPage() {
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [dateFrom, setDateFrom] = useState(todayIsoDate())
  const [dateTo, setDateTo] = useState(todayIsoDate())
  const [departmentId, setDepartmentId] = useState('')
  const [company, setCompany] = useState('')
  const [reportType, setReportType] = useState('daily')
  const [rows, setRows] = useState<VisitorListItemDto[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [exporting, setExporting] = useState<string | null>(null)

  useEffect(() => {
    void mastersApi.departments().then(setDepartments)
  }, [])

  function params() {
    return {
      reportType,
      dateFrom,
      dateTo,
      departmentId: departmentId || undefined,
      company: company || undefined,
    }
  }

  async function runReport() {
    setLoading(true)
    setError(null)
    try {
      const data = await reportsApi.visitorsJson(params())
      const list = Array.isArray(data)
        ? data
        : ((data as { items?: VisitorListItemDto[] })?.items ?? [])
      setRows(list as VisitorListItemDto[])
    } catch (e) {
      setError(apiErrorMessage(e))
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

  /** Direct authenticated download links for browsers that support them less reliably — use button handlers. */
  function exportHref(format: string) {
    const q = new URLSearchParams()
    Object.entries(params()).forEach(([k, v]) => {
      if (v != null && v !== '') q.set(k, String(v))
    })
    q.set('format', format)
    const token = getStoredToken()
    return `/api/reports/visitors?${q.toString()}${token ? '' : ''}`
  }

  return (
    <div>
      <PageHeader title="Reports" subtitle="Visitor activity reports with export" />

      <Panel className="mb-4">
        <div className="grid gap-3 md:grid-cols-3 xl:grid-cols-5">
          <div>
            <FieldLabel>Report type</FieldLabel>
            <TextSelect value={reportType} onChange={(e) => setReportType(e.target.value)}>
              <option value="daily">Daily</option>
              <option value="range">Date range</option>
              <option value="department">By department</option>
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
            <FieldLabel>Department</FieldLabel>
            <TextSelect value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
              <option value="">All</option>
              {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
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
          <a className="hidden" href={exportHref('csv')} aria-hidden>
            csv
          </a>
        </div>
      </Panel>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {loading ? <Spinner /> : null}
      {!loading && rows.length === 0 ? <EmptyState title="Run a report to see results" /> : null}

      {!loading && rows.length > 0 ? (
        <Panel className="overflow-x-auto p-0">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr>
                <th className="px-4 py-3">Visitor</th>
                <th className="px-4 py-3">Company</th>
                <th className="px-4 py-3">Host</th>
                <th className="px-4 py-3">Date</th>
                <th className="px-4 py-3">Status</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((v) => (
                <tr key={v.visitId} className="border-t border-gray-100">
                  <td className="px-4 py-3 font-medium">{v.visitorName}</td>
                  <td className="px-4 py-3">{v.companyName}</td>
                  <td className="px-4 py-3">{v.hostName}</td>
                  <td className="px-4 py-3">{formatDate(v.visitDate)}</td>
                  <td className="px-4 py-3"><Badge className={statusBadgeClass(v.statusLabel)}>{v.statusLabel}</Badge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      ) : null}
    </div>
  )
}
