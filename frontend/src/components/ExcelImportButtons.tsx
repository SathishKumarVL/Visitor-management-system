import { useRef, useState } from 'react'
import axios from 'axios'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { MasterImportKind, MasterImportResultDto } from '../types/api'
import { Button } from './ui/Button'
import { Alert } from './ui/Panel'

function importError(e: unknown) {
  if (axios.isAxiosError(e) && e.response?.status === 403) {
    return 'You do not have permission to import this data.'
  }
  return apiErrorMessage(e)
}

/**
 * "Download template" + "Upload Excel" actions for a master table.
 * Results (added / updated / per-row errors) open in a dialog.
 */
export function ExcelImportButtons({
  kind,
  label,
  onImported,
}: {
  kind: MasterImportKind
  label: string
  onImported?: () => void | Promise<void>
}) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [downloading, setDownloading] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [result, setResult] = useState<MasterImportResultDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState(false)

  async function downloadTemplate() {
    setDownloading(true)
    try {
      const blob = await mastersApi.downloadImportTemplate(kind)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${kind}-template.xlsx`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch (e) {
      setResult(null)
      setError(importError(e))
      setOpen(true)
    } finally {
      setDownloading(false)
    }
  }

  async function upload(file: File | null) {
    if (fileRef.current) fileRef.current.value = ''
    if (!file) return
    setUploading(true)
    setError(null)
    setResult(null)
    try {
      const res = await mastersApi.importExcel(kind, file)
      setResult(res)
      if (res.created + res.updated > 0) await onImported?.()
    } catch (e) {
      setError(importError(e))
    } finally {
      setUploading(false)
      setOpen(true)
    }
  }

  return (
    <>
      <Button variant="secondary" onClick={() => void downloadTemplate()} loading={downloading}>
        Download template
      </Button>
      <Button variant="secondary" onClick={() => fileRef.current?.click()} loading={uploading}>
        Upload Excel
      </Button>
      <input
        ref={fileRef}
        type="file"
        accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        className="hidden"
        onChange={(e) => void upload(e.target.files?.[0] ?? null)}
      />

      {open ? (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
          role="dialog"
          aria-modal="true"
          onClick={() => setOpen(false)}
        >
          <div
            className="max-h-[94dvh] w-full max-w-3xl overflow-y-auto rounded-2xl bg-white p-5 shadow-xl"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 className="mb-3 text-lg font-semibold text-steel">{label} import</h2>
            {error ? <Alert tone="error">{error}</Alert> : null}
            {result ? (
              <div className="space-y-3 text-sm">
                <div className="grid grid-cols-3 gap-2 text-center">
                  <Stat label="Added" value={result.created} tone="text-emerald-700" />
                  <Stat label="Updated" value={result.updated} tone="text-sky-700" />
                  <Stat label="Failed" value={result.failed} tone={result.failed ? 'text-red-600' : 'text-ink-muted'} />
                </div>
                {result.totalRows === 0 ? (
                  <Alert tone="warning" persistent>No data rows were found. Fill the rows below the header in the Data sheet.</Alert>
                ) : null}
                {result.errors.length > 0 ? (
                  <div>
                    <div className="mb-1 font-medium text-ink">Rows that were not saved</div>
                    <ul className="grid divide-gray-100 rounded-xl border border-gray-200 sm:grid-cols-2">
                      {result.errors.map((err) => (
                        <li key={`${err.row}-${err.message}`} className="border-b border-gray-100 px-3 py-2">
                          <span className="font-mono text-xs text-ink-muted">Row {err.row}:</span>{' '}
                          {err.message}
                        </li>
                      ))}
                    </ul>
                    <p className="mt-2 text-xs text-ink-muted">
                      Fix these rows in the Excel file and upload again. Rows already saved will be updated, not duplicated.
                    </p>
                  </div>
                ) : null}
              </div>
            ) : null}
            <div className="mt-4 flex justify-end">
              <Button onClick={() => setOpen(false)}>Close</Button>
            </div>
          </div>
        </div>
      ) : null}
    </>
  )
}

function Stat({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className="rounded-xl bg-gray-50 px-3 py-3">
      <div className={`text-2xl font-bold ${tone}`}>{value}</div>
      <div className="text-xs uppercase tracking-wide text-ink-muted">{label}</div>
    </div>
  )
}
