import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, sitesApi } from '../lib/api'
import type { SiteDto, SiteUpsertRequest } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextArea } from '../components/ui/Field'

const blank = (): SiteUpsertRequest => ({
  name: '',
  code: '',
  address: '',
  isActive: true,
  isDefault: false,
})

export function SitesPage() {
  const [sites, setSites] = useState<SiteDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<SiteDto | null>(null)
  const [form, setForm] = useState<SiteUpsertRequest>(blank())
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setSites(await sitesApi.list(false))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function startCreate() {
    setEditing(null)
    setForm(blank())
    setMessage(null)
  }

  function startEdit(site: SiteDto) {
    setEditing(site)
    setMessage(null)
    setForm({
      name: site.name,
      code: site.code || '',
      address: site.address || '',
      isActive: site.isActive,
      isDefault: site.isDefault,
    })
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setMessage(null)
    setError(null)
    try {
      if (editing) await sitesApi.update(editing.id, form)
      else await sitesApi.create(form)
      setMessage(editing ? 'Site updated.' : 'Site created.')
      setEditing(null)
      setForm(blank())
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function deactivate(site: SiteDto) {
    setError(null)
    setMessage(null)
    try {
      await sitesApi.deactivate(site.id)
      setMessage(`${site.name} deactivated.`)
      await load()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  return (
    <div>
      <PageHeader
        title="Sites"
        subtitle="Each workplace, branch or plant where this system is used"
        actions={<Button variant="amber" onClick={startCreate}>Add new</Button>}
      />

      <div className="mb-4">
        <Alert tone="info">
          Users assigned to a site only see visitors and reports for that site.
          Leave a user unassigned to give them visibility across every site.
        </Alert>
      </div>

      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <div className="grid gap-4 xl:grid-cols-[1fr_360px]">
        <Panel className="overflow-x-auto p-0">
          {loading ? <Spinner /> : null}
          {!loading && sites.length === 0 ? (
            <div className="p-4"><EmptyState title="No sites" description="Create your first workplace to get started." /></div>
          ) : null}
          {!loading && sites.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <th className="px-4 py-3">Site</th>
                  <th className="px-4 py-3">Users</th>
                  <th className="px-4 py-3">Areas</th>
                  <th className="px-4 py-3">Inside</th>
                  <th className="px-4 py-3">Active</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {sites.map((s) => (
                  <tr key={s.id} className="border-t border-gray-100">
                    <td className="px-4 py-3">
                      <div className="font-medium">
                        {s.name}
                        {s.isDefault ? <span className="ml-2 text-xs font-normal text-teal-700">Default</span> : null}
                      </div>
                      <div className="text-xs text-gray-500">
                        {[s.code, s.address].filter(Boolean).join(' · ') || '—'}
                      </div>
                    </td>
                    <td className="px-4 py-3">{s.userCount}</td>
                    <td className="px-4 py-3">{s.locationCount}</td>
                    <td className="px-4 py-3">{s.insideCount}</td>
                    <td className="px-4 py-3">{s.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        <Button size="sm" variant="secondary" onClick={() => startEdit(s)}>Edit</Button>
                        {s.isActive ? (
                          <Button size="sm" variant="danger" onClick={() => void deactivate(s)}>Deactivate</Button>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </Panel>

        <Panel>
          <h2 className="mb-3 font-semibold text-steel">{editing ? `Edit ${editing.name}` : 'Create site'}</h2>
          <form onSubmit={save} className="space-y-3">
            <div>
              <FieldLabel htmlFor="site-name">Name *</FieldLabel>
              <TextInput
                id="site-name"
                required
                maxLength={150}
                placeholder="Chennai Plant"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
              />
            </div>
            <div>
              <FieldLabel htmlFor="site-code">Code</FieldLabel>
              <TextInput
                id="site-code"
                maxLength={50}
                placeholder="CHN"
                value={form.code || ''}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
              />
            </div>
            <div>
              <FieldLabel htmlFor="site-address">Address</FieldLabel>
              <TextArea
                id="site-address"
                rows={3}
                maxLength={300}
                value={form.address || ''}
                onChange={(e) => setForm({ ...form, address: e.target.value })}
              />
            </div>
            <label className="flex min-h-11 items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              />
              Active
            </label>
            <label className="flex min-h-11 items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={form.isDefault}
                onChange={(e) => setForm({ ...form, isDefault: e.target.checked })}
              />
              Default site for new visitors
            </label>
            <div className="flex gap-2">
              <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
              {editing ? (
                <Button type="button" variant="secondary" onClick={startCreate}>Cancel</Button>
              ) : null}
            </div>
          </form>
        </Panel>
      </div>
    </div>
  )
}
