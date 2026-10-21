import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, settingsApi } from '../lib/api'
import type {
  LookupMatchTarget,
  LookupSavedFieldDto,
  LookupSourceDto,
  LookupSourceUpsertRequest,
  LookupTestResultDto,
} from '../types/api'
import { formatDateTime } from '../lib/utils'
import { Button } from './ui/Button'
import { Alert, Spinner } from './ui/Panel'
import { FieldLabel, PasswordInput, TextArea, TextInput, TextSelect } from './ui/Field'
import { Modal } from './ui/Modal'

const MAX_SAVED_FIELDS = 8

const FIELD_TOKEN = /\{field:([^{}"]+)\}/gi

/** Labels used as {field:Label} in the URL or body, so the test can ask for sample values. */
function fieldNames(...templates: (string | null | undefined)[]): string[] {
  const names: string[] = []
  for (const t of templates) {
    for (const m of (t ?? '').matchAll(FIELD_TOKEN)) {
      const name = m[1].trim()
      if (name && !names.some((n) => n.toLowerCase() === name.toLowerCase())) names.push(name)
    }
  }
  return names
}

const EMPTY_FORM: LookupSourceUpsertRequest = {
  name: '',
  searchUrl: '',
  httpMethod: 'GET',
  bodyTemplate: '',
  apiKeyHeader: 'X-Api-Key',
  apiKey: '',
  clearApiKey: false,
  resultsPath: '',
  idPath: '',
  displayTemplate: '',
  savedFields: [],
  matchPath: '',
  matchAgainst: null,
  blockOnMismatch: false,
  minChars: 3,
  timeoutSeconds: 5,
  isActive: true,
}

function toForm(s: LookupSourceDto): LookupSourceUpsertRequest {
  return {
    name: s.name,
    searchUrl: s.searchUrl,
    httpMethod: s.httpMethod === 'POST' ? 'POST' : 'GET',
    bodyTemplate: s.bodyTemplate ?? '',
    apiKeyHeader: s.apiKeyHeader ?? '',
    apiKey: '',
    clearApiKey: false,
    resultsPath: s.resultsPath ?? '',
    idPath: s.idPath,
    displayTemplate: s.displayTemplate,
    savedFields: s.savedFields.map((f) => ({ ...f })),
    matchPath: s.matchPath ?? '',
    matchAgainst: s.matchAgainst ?? null,
    blockOnMismatch: s.blockOnMismatch,
    minChars: s.minChars,
    timeoutSeconds: s.timeoutSeconds,
    isActive: s.isActive,
  }
}

function hostOf(url: string): string {
  try {
    return new URL(url.replace(/\{query\}/gi, 'x')).host
  } catch {
    return url
  }
}

function Help({ children }: { children: React.ReactNode }) {
  return <p className="mt-1 text-xs text-ink-muted">{children}</p>
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="sm:col-span-2 border-t border-border/70 pt-4 text-xs font-semibold uppercase tracking-[0.08em] text-ink-muted first:border-t-0 first:pt-0">
      {children}
    </h3>
  )
}

/** SuperAdmin setup of external APIs (e.g. a school's student list) that purpose fields can search. */
export function LookupSourcesPanel() {
  const [sources, setSources] = useState<LookupSourceDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<LookupSourceDto | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [form, setForm] = useState<LookupSourceUpsertRequest>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [testQuery, setTestQuery] = useState('')
  const [testContext, setTestContext] = useState<Record<string, string>>({})
  const [testing, setTesting] = useState(false)
  const testFields = fieldNames(form.searchUrl, form.httpMethod === 'POST' ? form.bodyTemplate : null)
  const [testResult, setTestResult] = useState<LookupTestResultDto | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setSources(await settingsApi.listLookupSources())
      setError(null)
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to load lookup sources.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function patch(p: Partial<LookupSourceUpsertRequest>) {
    setForm((f) => ({ ...f, ...p }))
  }

  function patchSaved(index: number, p: Partial<LookupSavedFieldDto>) {
    setForm((f) => ({ ...f, savedFields: f.savedFields.map((s, i) => (i === index ? { ...s, ...p } : s)) }))
  }

  function open(source: LookupSourceDto | null) {
    setEditing(source)
    setForm(source ? toForm(source) : EMPTY_FORM)
    setFormError(null)
    setTestResult(null)
    setTestQuery('')
    setTestContext({})
    setFormOpen(true)
  }

  function cleaned(): LookupSourceUpsertRequest {
    return {
      ...form,
      name: form.name.trim(),
      searchUrl: form.searchUrl.trim(),
      bodyTemplate: form.httpMethod === 'POST' ? form.bodyTemplate?.trim() || null : null,
      apiKeyHeader: form.apiKeyHeader?.trim() || null,
      apiKey: form.apiKey?.trim() || null,
      resultsPath: form.resultsPath?.trim() || null,
      idPath: form.idPath.trim(),
      displayTemplate: form.displayTemplate.trim(),
      savedFields: form.savedFields
        .map((f) => ({ path: f.path.trim(), label: f.label.trim() }))
        .filter((f) => f.path.length > 0),
      matchPath: form.matchAgainst ? form.matchPath?.trim() || null : null,
      blockOnMismatch: !!form.matchAgainst && form.blockOnMismatch,
    }
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setFormError(null)
    try {
      await settingsApi.saveLookupSource(editing?.id ?? null, cleaned())
      setFormOpen(false)
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Unable to save the lookup source.'))
    } finally {
      setSaving(false)
    }
  }

  async function runTest() {
    if (!testQuery.trim()) {
      setTestResult({ ok: false, error: 'Type something to search for.', recordCount: 0, matches: [] })
      return
    }
    setTesting(true)
    try {
      setTestResult(await settingsApi.testLookupSource(editing?.id ?? null, cleaned(), testQuery.trim(), testContext))
    } catch (err) {
      setTestResult({ ok: false, error: apiErrorMessage(err, 'The test could not run.'), recordCount: 0, matches: [] })
    } finally {
      setTesting(false)
    }
  }

  async function remove(source: LookupSourceDto) {
    if (!window.confirm(`Delete "${source.name}"? Purpose fields using it must be changed first.`)) return
    try {
      await settingsApi.deleteLookupSource(source.id)
      await load()
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to delete the lookup source.'))
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Lookup sources</h2>
          <p className="mt-1 max-w-xl text-xs text-ink-muted">
            Connect an outside system, such as a school&apos;s student list, so reception can search it from a purpose&apos;s
            extra field instead of typing. The server calls the API; the key never reaches the browser. After saving a
            source, open Masters, then Purposes, and set a field&apos;s type to this source.
          </p>
        </div>
        <Button type="button" onClick={() => open(null)}>
          New lookup source
        </Button>
      </div>

      {error ? <Alert tone="error">{error}</Alert> : null}

      {loading ? (
        <Spinner />
      ) : sources.length === 0 ? (
        <p className="text-sm text-ink-muted">No lookup sources yet.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-xs uppercase tracking-wide text-ink-muted">
              <tr>
                <th className="py-2 pr-3">Name</th>
                <th className="py-2 pr-3">Calls</th>
                <th className="py-2 pr-3">Visitor check</th>
                <th className="py-2 pr-3">Status</th>
                <th className="py-2" />
              </tr>
            </thead>
            <tbody>
              {sources.map((s) => (
                <tr key={s.id} className="border-t border-border/70">
                  <td className="py-2 pr-3 font-medium text-ink">
                    {s.name}
                    {s.updatedAt ? (
                      <div className="text-xs font-normal text-ink-muted">Updated {formatDateTime(s.updatedAt)}</div>
                    ) : null}
                  </td>
                  <td className="py-2 pr-3 text-xs">
                    <span className="font-mono">
                      {s.httpMethod} {hostOf(s.searchUrl)}
                    </span>
                    <div className="text-ink-muted">{s.hasApiKey ? `Key in ${s.apiKeyHeader}` : 'No API key'}</div>
                  </td>
                  <td className="py-2 pr-3 text-xs">
                    {s.matchAgainst
                      ? `Visitor's ${s.matchAgainst} in ${s.matchPath}${s.blockOnMismatch ? ' (blocks)' : ' (warns)'}`
                      : 'Off'}
                  </td>
                  <td className="py-2 pr-3">
                    {s.isActive ? (
                      <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">Active</span>
                    ) : (
                      <span className="rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-ink-muted">Off</span>
                    )}
                  </td>
                  <td className="py-2 text-right whitespace-nowrap">
                    <Button type="button" variant="secondary" size="sm" onClick={() => open(s)}>
                      Edit
                    </Button>{' '}
                    <Button type="button" variant="ghost" size="sm" onClick={() => void remove(s)}>
                      Delete
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Modal
        open={formOpen}
        title={editing ? `Edit ${editing.name}` : 'New lookup source'}
        subtitle="Fill in the API details, then use Test to check them against a real search."
        onClose={() => setFormOpen(false)}
        size="lg"
      >
        <form onSubmit={save} className="grid gap-4 sm:grid-cols-2">
          {formError ? (
            <div className="sm:col-span-2">
              <Alert tone="error">{formError}</Alert>
            </div>
          ) : null}

          <SectionTitle>API</SectionTitle>
          <div>
            <FieldLabel htmlFor="lsName">Name *</FieldLabel>
            <TextInput
              id="lsName"
              required
              maxLength={100}
              value={form.name}
              placeholder="e.g. School students"
              onChange={(e) => patch({ name: e.target.value })}
            />
          </div>
          <label className="flex items-center gap-2 self-end pb-3 text-sm">
            <input
              type="checkbox"
              className="accent-primary"
              checked={form.isActive}
              onChange={(e) => patch({ isActive: e.target.checked })}
            />
            Active (offered while registering)
          </label>
          <div className="sm:col-span-2 flex flex-wrap gap-2">
            <div className="w-28">
              <FieldLabel htmlFor="lsMethod">Method</FieldLabel>
              <TextSelect
                id="lsMethod"
                value={form.httpMethod}
                onChange={(e) => patch({ httpMethod: e.target.value === 'POST' ? 'POST' : 'GET' })}
              >
                <option value="GET">GET</option>
                <option value="POST">POST</option>
              </TextSelect>
            </div>
            <div className="min-w-0 flex-1">
              <FieldLabel htmlFor="lsUrl">URL *</FieldLabel>
              <TextInput
                id="lsUrl"
                required
                maxLength={1000}
                value={form.searchUrl}
                placeholder={
                  form.httpMethod === 'POST'
                    ? 'https://api.school.example.com/api/Student/StudentBasicInfo'
                    : 'https://school.example.com/api/students?search={query}'
                }
                onChange={(e) => patch({ searchUrl: e.target.value })}
              />
            </div>
            <div className="w-full">
              <Help>
                {form.httpMethod === 'GET' ? (
                  <>
                    Put <code className="rounded bg-gray-100 px-1">{'{query}'}</code> where the typed text goes.{' '}
                  </>
                ) : null}
                Must start with https://. Addresses inside the office network are blocked.
              </Help>
            </div>
          </div>
          {form.httpMethod === 'POST' ? (
            <div className="sm:col-span-2">
              <FieldLabel htmlFor="lsBody">Request body (JSON) *</FieldLabel>
              <TextArea
                id="lsBody"
                maxLength={4000}
                rows={5}
                className="font-mono text-xs"
                value={form.bodyTemplate ?? ''}
                placeholder={'{\n  "class": "{field:Class}",\n  "section": "{field:Section}",\n  "fullName": "{query}"\n}'}
                onChange={(e) => patch({ bodyTemplate: e.target.value })}
              />
              <Help>
                <code className="rounded bg-gray-100 px-1">{'{query}'}</code> is the text reception types.{' '}
                <code className="rounded bg-gray-100 px-1">{'{field:Class}'}</code> is the value of the purpose&apos;s extra
                field named Class (add it as a Text field before the search field). Keep placeholders inside quotes; a
                field left empty is sent as &quot;&quot;.
              </Help>
            </div>
          ) : null}
          <div>
            <FieldLabel htmlFor="lsHeader">API key header</FieldLabel>
            <TextInput
              id="lsHeader"
              maxLength={100}
              value={form.apiKeyHeader ?? ''}
              placeholder="X-Api-Key"
              onChange={(e) => patch({ apiKeyHeader: e.target.value })}
            />
            <Help>For a bearer token use Authorization and enter the key as &quot;Bearer …&quot;.</Help>
          </div>
          <div>
            <FieldLabel htmlFor="lsKey">API key</FieldLabel>
            <PasswordInput
              id="lsKey"
              autoComplete="new-password"
              maxLength={2000}
              value={form.apiKey ?? ''}
              disabled={form.clearApiKey}
              placeholder={editing?.hasApiKey ? 'Saved; leave blank to keep' : 'Paste the key from the provider'}
              onChange={(e) => patch({ apiKey: e.target.value })}
            />
            {editing?.hasApiKey ? (
              <label className="mt-1 flex items-center gap-2 text-xs text-ink-muted">
                <input
                  type="checkbox"
                  className="accent-primary"
                  checked={!!form.clearApiKey}
                  onChange={(e) => patch({ clearApiKey: e.target.checked, apiKey: '' })}
                />
                Remove the saved key
              </label>
            ) : null}
          </div>

          <SectionTitle>Reading the answer</SectionTitle>
          <div>
            <FieldLabel htmlFor="lsResults">Results path</FieldLabel>
            <TextInput
              id="lsResults"
              maxLength={200}
              value={form.resultsPath ?? ''}
              placeholder="data.students"
              onChange={(e) => patch({ resultsPath: e.target.value })}
            />
            <Help>Where the list of records is. Leave blank when the answer is the list itself.</Help>
          </div>
          <div>
            <FieldLabel htmlFor="lsId">ID property *</FieldLabel>
            <TextInput
              id="lsId"
              required
              maxLength={200}
              value={form.idPath}
              placeholder="admissionNo"
              onChange={(e) => patch({ idPath: e.target.value })}
            />
            <Help>Unique number of each record, saved with the visit.</Help>
          </div>
          <div className="sm:col-span-2">
            <FieldLabel htmlFor="lsDisplay">Display text *</FieldLabel>
            <TextInput
              id="lsDisplay"
              required
              maxLength={300}
              value={form.displayTemplate}
              placeholder="{name} - Class {class}-{section} (Adm {admissionNo})"
              onChange={(e) => patch({ displayTemplate: e.target.value })}
            />
            <Help>
              Shown in the search list and saved as the field&apos;s value. Use property names in braces; nested ones with
              dots, e.g. <code className="rounded bg-gray-100 px-1">{'{class.name}'}</code>.
            </Help>
          </div>
          <div className="sm:col-span-2">
            <div className="mb-1.5 flex items-center justify-between gap-2">
              <span className="text-sm font-medium text-ink">Also save these properties</span>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabled={form.savedFields.length >= MAX_SAVED_FIELDS}
                onClick={() => patch({ savedFields: [...form.savedFields, { path: '', label: '' }] })}
              >
                + Add property
              </Button>
            </div>
            {form.savedFields.length === 0 ? (
              <p className="text-xs text-ink-muted">Optional: shown on the visitor&apos;s details, e.g. Class or Section.</p>
            ) : (
              <div className="space-y-2">
                {form.savedFields.map((f, i) => (
                  <div key={i} className="flex flex-wrap items-center gap-2">
                    <TextInput
                      aria-label="Property path"
                      className="min-w-0 flex-1"
                      maxLength={200}
                      value={f.path}
                      placeholder="Property, e.g. class"
                      onChange={(e) => patchSaved(i, { path: e.target.value })}
                    />
                    <TextInput
                      aria-label="Label"
                      className="min-w-0 flex-1"
                      maxLength={100}
                      value={f.label}
                      placeholder="Label, e.g. Class"
                      onChange={(e) => patchSaved(i, { label: e.target.value })}
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => patch({ savedFields: form.savedFields.filter((_, j) => j !== i) })}
                    >
                      Remove
                    </Button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <SectionTitle>Visitor check</SectionTitle>
          <div>
            <FieldLabel htmlFor="lsAgainst">Compare</FieldLabel>
            <TextSelect
              id="lsAgainst"
              value={form.matchAgainst ?? ''}
              onChange={(e) => patch({ matchAgainst: (e.target.value || null) as LookupMatchTarget | null })}
            >
              <option value="">No check</option>
              <option value="mobile">Visitor&apos;s mobile number</option>
              <option value="email">Visitor&apos;s email</option>
            </TextSelect>
          </div>
          <div>
            <FieldLabel htmlFor="lsMatchPath">With the record&apos;s property{form.matchAgainst ? ' *' : ''}</FieldLabel>
            <TextInput
              id="lsMatchPath"
              maxLength={200}
              required={!!form.matchAgainst}
              disabled={!form.matchAgainst}
              value={form.matchPath ?? ''}
              placeholder="guardians.mobile"
              onChange={(e) => patch({ matchPath: e.target.value })}
            />
            <Help>A list works too: every guardian&apos;s mobile is checked.</Help>
          </div>
          {form.matchAgainst ? (
            <label className="sm:col-span-2 flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                className="mt-1 accent-primary"
                checked={form.blockOnMismatch}
                onChange={(e) => patch({ blockOnMismatch: e.target.checked })}
              />
              <span>
                Block registration when it does not match
                <span className="block text-xs text-ink-muted">
                  Otherwise reception sees a warning and the visit is saved with &quot;not on record&quot;. Values typed
                  by hand (when the API is down) are always allowed and saved as not verified.
                </span>
              </span>
            </label>
          ) : null}

          <SectionTitle>Search</SectionTitle>
          <div>
            <FieldLabel htmlFor="lsMin">Start searching after (letters)</FieldLabel>
            <TextInput
              id="lsMin"
              type="number"
              min={1}
              max={10}
              value={form.minChars}
              onChange={(e) => patch({ minChars: Number(e.target.value) || 1 })}
            />
          </div>
          <div>
            <FieldLabel htmlFor="lsTimeout">Give up after (seconds)</FieldLabel>
            <TextInput
              id="lsTimeout"
              type="number"
              min={2}
              max={15}
              value={form.timeoutSeconds}
              onChange={(e) => patch({ timeoutSeconds: Number(e.target.value) || 5 })}
            />
          </div>

          <SectionTitle>Test</SectionTitle>
          <div className="sm:col-span-2 flex flex-wrap items-end gap-2">
            <div className="min-w-0 flex-1">
              <FieldLabel htmlFor="lsTest">Search for</FieldLabel>
              <TextInput
                id="lsTest"
                maxLength={100}
                value={testQuery}
                placeholder="e.g. a student's name"
                onChange={(e) => setTestQuery(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault()
                    void runTest()
                  }
                }}
              />
            </div>
            {testFields.map((name) => (
              <div key={name} className="w-32">
                <FieldLabel htmlFor={`lsTest-${name}`}>{name}</FieldLabel>
                <TextInput
                  id={`lsTest-${name}`}
                  maxLength={200}
                  value={testContext[name] ?? ''}
                  onChange={(e) => setTestContext((c) => ({ ...c, [name]: e.target.value }))}
                />
              </div>
            ))}
            <Button type="button" variant="secondary" loading={testing} onClick={() => void runTest()}>
              Run test
            </Button>
          </div>
          {testResult ? (
            <div className="sm:col-span-2 space-y-2 rounded-xl border border-border/70 bg-gray-50 p-3 text-sm">
              {testResult.error ? (
                <p className={testResult.ok ? 'text-amber-700' : 'text-danger'}>{testResult.error}</p>
              ) : (
                <p className="text-emerald-700">
                  Found {testResult.recordCount} record{testResult.recordCount === 1 ? '' : 's'}
                  {testResult.recordCount > testResult.matches.length ? ` (showing ${testResult.matches.length})` : ''}.
                </p>
              )}
              {testResult.matches.length > 0 ? (
                <ul className="divide-y divide-border/70 rounded-lg bg-white">
                  {testResult.matches.slice(0, 5).map((m, i) => (
                    <li key={i} className="px-3 py-2">
                      <div className="font-medium text-ink">{m.display || '(empty display text)'}</div>
                      <div className="text-xs text-ink-muted">
                        ID: {m.recordId || '(empty)'}
                        {m.details.map((d) => ` · ${d.label}: ${d.value}`).join('')}
                      </div>
                      {m.hasMatchCheck ? (
                        <div className="text-xs text-ink-muted">
                          Check values: {m.matchValues && m.matchValues.length > 0 ? m.matchValues.join(', ') : '(none found)'}
                        </div>
                      ) : null}
                    </li>
                  ))}
                </ul>
              ) : null}
              {testResult.rawSample ? (
                <details>
                  <summary className="cursor-pointer text-xs font-medium text-ink-muted">
                    Raw answer{testResult.statusCode ? ` (HTTP ${testResult.statusCode})` : ''}
                  </summary>
                  <pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-white p-2 text-xs">
                    {testResult.rawSample}
                  </pre>
                </details>
              ) : null}
            </div>
          ) : null}

          <div className="sm:col-span-2 flex justify-end gap-2 pt-2">
            <Button type="button" variant="secondary" onClick={() => setFormOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" loading={saving}>
              Save
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  )
}
