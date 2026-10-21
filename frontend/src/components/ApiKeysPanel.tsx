import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, settingsApi } from '../lib/api'
import type { IntegrationApiKeyDto, IntegrationScope } from '../types/api'
import { formatDateTime } from '../lib/utils'
import { Button } from './ui/Button'
import { Alert, Spinner } from './ui/Panel'
import { FieldLabel, TextInput } from './ui/Field'
import { Modal } from './ui/Modal'
import { Pagination } from './ui/Pagination'
import { usePagination } from '../lib/usePagination'

const SCOPES: { id: IntegrationScope; label: string; help: string }[] = [
  { id: 'reports', label: 'Read reports', help: 'GET /api/integration/reports/visitors' },
  { id: 'employees', label: 'Read and update employees', help: 'GET / POST /api/integration/employees' },
]

type Endpoint = { method: string; path: string; label: string; scope?: IntegrationScope }

const ENDPOINTS: Endpoint[] = [
  { method: 'GET', path: '/masters', label: 'Locations and departments' },
  { method: 'POST', path: '/employees', label: 'Create or update employees', scope: 'employees' },
  { method: 'GET', path: '/employees', label: 'List employees', scope: 'employees' },
  { method: 'GET', path: '/reports/visitors', label: 'Visitor report', scope: 'reports' },
]

/** The browser reaches the API at /api on this same origin, so the integration URL is built from it. */
function integrationBaseUrl(): string {
  return `${window.location.origin}/api/integration`
}

function CopyButton({ text, label = 'Copy' }: { text: string; label?: string }) {
  const [copied, setCopied] = useState(false)
  async function copy() {
    let ok = false
    try {
      await navigator.clipboard.writeText(text)
      ok = true
    } catch {
      // The Clipboard API needs HTTPS or localhost; fall back for plain-HTTP access by IP or host name.
      const area = document.createElement('textarea')
      area.value = text
      area.style.position = 'fixed'
      area.style.opacity = '0'
      document.body.appendChild(area)
      area.select()
      ok = document.execCommand('copy')
      document.body.removeChild(area)
    }
    setCopied(ok)
    if (ok) window.setTimeout(() => setCopied(false), 2000)
  }
  return (
    <Button type="button" variant="secondary" onClick={() => void copy()}>
      {copied ? 'Copied' : label}
    </Button>
  )
}

function CopyRow({ label, value, mono = true }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="mb-1 text-xs font-medium text-ink-muted">{label}</div>
      <div className="flex items-center gap-2">
        <div className={`min-w-0 flex-1 break-all rounded-xl bg-gray-50 p-2.5 text-sm ${mono ? 'font-mono' : ''}`}>
          {value}
        </div>
        <CopyButton text={value} />
      </div>
    </div>
  )
}

/** SuperAdmin management of API keys used by other applications to call /api/integration. */
export function ApiKeysPanel() {
  const [keys, setKeys] = useState<IntegrationApiKeyDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [name, setName] = useState('')
  const [scopes, setScopes] = useState<IntegrationScope[]>(['reports', 'employees'])
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [createdKey, setCreatedKey] = useState<string | null>(null)
  const { pageItems, pagination } = usePagination(keys, 10)
  const baseUrl = integrationBaseUrl()
  const createdEndpoints = ENDPOINTS.filter((e) => !e.scope || scopes.includes(e.scope))
  const connectionDetails = createdKey
    ? [
        `Base URL: ${baseUrl}`,
        `Header:   X-Api-Key: ${createdKey}`,
        '',
        'Endpoints:',
        ...createdEndpoints.map((e) => `  ${e.method.padEnd(4)} ${baseUrl}${e.path}   (${e.label})`),
      ].join('\n')
    : ''

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setKeys(await settingsApi.listApiKeys())
      setError(null)
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to load API keys.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function openForm() {
    setName('')
    setScopes(['reports', 'employees'])
    setFormError(null)
    setCreatedKey(null)
    setFormOpen(true)
  }

  async function create(e: FormEvent) {
    e.preventDefault()
    if (scopes.length === 0) {
      setFormError('Choose at least one permission.')
      return
    }
    setSaving(true)
    setFormError(null)
    try {
      const created = await settingsApi.createApiKey(name.trim(), scopes)
      setCreatedKey(created.plainKey)
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Unable to create the API key.'))
    } finally {
      setSaving(false)
    }
  }

  async function revoke(key: IntegrationApiKeyDto) {
    if (!window.confirm(`Revoke "${key.name}"? Any application using it will stop working immediately.`)) return
    try {
      await settingsApi.revokeApiKey(key.id)
      await load()
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to revoke the API key.'))
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">API keys</h2>
          <p className="mt-1 max-w-xl text-xs text-ink-muted">
            Let another application fetch report data or send employees. It sends the key in an{' '}
            <code className="rounded bg-gray-100 px-1">X-Api-Key</code> header to{' '}
            <code className="rounded bg-gray-100 px-1">/api/integration/…</code>. Keys cannot sign in or reach any other part
            of the system.
          </p>
          <div className="mt-2 flex max-w-xl items-center gap-2">
            <span className="shrink-0 text-xs font-medium text-ink-muted">API URL</span>
            <code className="min-w-0 flex-1 truncate rounded bg-gray-100 px-2 py-1 text-xs">{baseUrl}</code>
            <CopyButton text={baseUrl} />
          </div>
        </div>
        <Button type="button" onClick={openForm}>
          New API key
        </Button>
      </div>

      {error ? <Alert tone="error">{error}</Alert> : null}

      {loading ? (
        <Spinner />
      ) : keys.length === 0 ? (
        <p className="text-sm text-ink-muted">No API keys yet.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-xs uppercase tracking-wide text-ink-muted">
              <tr>
                <th className="py-2 pr-3">Name</th>
                <th className="py-2 pr-3">Key</th>
                <th className="py-2 pr-3">Permissions</th>
                <th className="py-2 pr-3">Last used</th>
                <th className="py-2 pr-3">Status</th>
                <th className="py-2" />
              </tr>
            </thead>
            <tbody>
              {pageItems.map((k) => (
                <tr key={k.id} className="border-t border-border/70">
                  <td className="py-2 pr-3 font-medium text-ink">
                    {k.name}
                    <div className="text-xs font-normal text-ink-muted">
                      Created {formatDateTime(k.createdAt)}
                      {k.createdBy ? ` by ${k.createdBy}` : ''}
                    </div>
                  </td>
                  <td className="py-2 pr-3 font-mono text-xs">{k.keyPrefix}…</td>
                  <td className="py-2 pr-3 text-xs">
                    {k.scopes.map((s) => SCOPES.find((x) => x.id === s)?.label ?? s).join(', ')}
                  </td>
                  <td className="py-2 pr-3 text-xs">{k.lastUsedAt ? formatDateTime(k.lastUsedAt) : 'Never'}</td>
                  <td className="py-2 pr-3">
                    {k.isActive ? (
                      <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">Active</span>
                    ) : (
                      <span className="rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-ink-muted">
                        Revoked {k.revokedAt ? formatDateTime(k.revokedAt) : ''}
                      </span>
                    )}
                  </td>
                  <td className="py-2 text-right">
                    {k.isActive ? (
                      <Button type="button" variant="secondary" onClick={() => void revoke(k)}>
                        Revoke
                      </Button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination {...pagination} className="px-0" />
        </div>
      )}

      <Modal
        open={formOpen}
        title={createdKey ? 'API key created' : 'New API key'}
        subtitle={createdKey ? undefined : 'Give the key a name that identifies the application using it.'}
        onClose={() => setFormOpen(false)}
        size="lg"
      >
        {createdKey ? (
          <div className="space-y-4 text-sm">
            <Alert tone="success" persistent>
              Copy the key and URL now and give them to the other application. The key will not be shown again.
            </Alert>
            <CopyRow label="API key (send in the X-Api-Key header)" value={createdKey} />
            <CopyRow label="API URL" value={baseUrl} />
            <div>
              <div className="mb-1 text-xs font-medium text-ink-muted">Endpoints this key can use</div>
              <ul className="space-y-2">
                {createdEndpoints.map((e) => (
                  <li key={`${e.method} ${e.path}`} className="flex items-center gap-2">
                    <span className="w-12 shrink-0 rounded bg-primary/10 px-1.5 py-0.5 text-center font-mono text-xs font-semibold text-primary-deep">
                      {e.method}
                    </span>
                    <div className="min-w-0 flex-1">
                      <div className="break-all font-mono text-xs text-ink">{`${baseUrl}${e.path}`}</div>
                      <div className="text-xs text-ink-muted">{e.label}</div>
                    </div>
                    <CopyButton text={`${baseUrl}${e.path}`} />
                  </li>
                ))}
              </ul>
            </div>
            <div className="flex flex-wrap justify-end gap-2">
              <CopyButton text={connectionDetails} label="Copy all details" />
              <Button type="button" onClick={() => setFormOpen(false)}>
                Done
              </Button>
            </div>
          </div>
        ) : (
          <form onSubmit={create} className="space-y-4">
            {formError ? <Alert tone="error">{formError}</Alert> : null}
            <div>
              <FieldLabel htmlFor="apiKeyName">Name *</FieldLabel>
              <TextInput
                id="apiKeyName"
                required
                maxLength={100}
                value={name}
                placeholder="e.g. HR system"
                onChange={(e) => setName(e.target.value)}
              />
            </div>
            <fieldset className="grid gap-x-4 gap-y-2 sm:grid-cols-2">
              <legend className="mb-2 text-sm font-medium text-ink">Permissions *</legend>
              {SCOPES.map((s) => (
                <label key={s.id} className="flex items-start gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="mt-1 accent-primary"
                    checked={scopes.includes(s.id)}
                    onChange={(e) =>
                      setScopes((cur) => (e.target.checked ? [...cur, s.id] : cur.filter((x) => x !== s.id)))
                    }
                  />
                  <span>
                    {s.label}
                    <span className="block font-mono text-xs text-ink-muted">{s.help}</span>
                  </span>
                </label>
              ))}
            </fieldset>
            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="secondary" onClick={() => setFormOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" loading={saving}>
                Create key
              </Button>
            </div>
          </form>
        )}
      </Modal>
    </div>
  )
}
