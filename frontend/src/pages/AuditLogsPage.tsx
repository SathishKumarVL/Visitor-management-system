import { useEffect, useState } from 'react'
import { auditApi, apiErrorMessage } from '../lib/api'
import type { AuditLogDto } from '../types/api'
import { Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { formatDateTime } from '../lib/utils'

export function AuditLogsPage() {
  const [items, setItems] = useState<AuditLogDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [action, setAction] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  async function load(p = page) {
    setLoading(true)
    setError('')
    try {
      const result = await auditApi.list(p, 50, action || undefined)
      setItems(result.items)
      setTotal(result.totalCount)
      setPage(result.page)
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to load audit logs'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load(1)
  }, [])

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold text-steel">Audit Logs</h1>
        <p className="text-sm text-gray-600">Read-only history of important system actions.</p>
      </div>

      <Panel className="flex flex-wrap items-end gap-3">
        <div>
          <label className="mb-1.5 block text-sm font-medium text-gray-700">Filter by action</label>
          <input
            className="min-h-11 w-full rounded-md border border-gray-200 bg-white px-3 text-gray-900 shadow-sm focus:border-steel focus:ring-1 focus:ring-steel"
            value={action}
            onChange={(e) => setAction(e.target.value)}
            placeholder="e.g. VisitorCheckedIn"
          />
        </div>
        <Button onClick={() => void load(1)}>Search</Button>
      </Panel>

      {error && <div className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}
      {loading ? (
        <Spinner label="Loading audit logs…" />
      ) : (
        <Panel className="overflow-x-auto p-0">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-steel text-white">
              <tr>
                <th className="px-3 py-3">When</th>
                <th className="px-3 py-3">User</th>
                <th className="px-3 py-3">Action</th>
                <th className="px-3 py-3">Entity</th>
                <th className="px-3 py-3">Description</th>
                <th className="px-3 py-3">IP</th>
              </tr>
            </thead>
            <tbody>
              {items.map((row) => (
                <tr key={row.id} className="border-b border-gray-100">
                  <td className="whitespace-nowrap px-3 py-3">{formatDateTime(row.createdAt)}</td>
                  <td className="px-3 py-3">{row.userName ?? '—'}</td>
                  <td className="px-3 py-3 font-medium">{row.action}</td>
                  <td className="px-3 py-3">
                    {row.entity}
                    {row.entityId ? ` #${row.entityId.slice(0, 8)}` : ''}
                  </td>
                  <td className="max-w-md px-3 py-3">{row.description ?? '—'}</td>
                  <td className="px-3 py-3">{row.ipAddress ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="flex items-center justify-between gap-3 p-3 text-sm text-gray-600">
            <span>
              Page {page} · {total} records
            </span>
            <div className="flex gap-2">
              <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => void load(page - 1)}>
                Previous
              </Button>
              <Button
                variant="secondary"
                size="sm"
                disabled={page * 50 >= total}
                onClick={() => void load(page + 1)}
              >
                Next
              </Button>
            </div>
          </div>
        </Panel>
      )}
    </div>
  )
}
