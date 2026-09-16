import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi, sitesApi } from '../lib/api'
import type { MasterItemDto, MasterUpsertRequest, SiteDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'

type MasterKind = 'departments' | 'purposes' | 'locations'

const config: Record<MasterKind, {
  title: string
  list: (activeOnly?: boolean) => Promise<MasterItemDto[]>
  create: (body: MasterUpsertRequest) => Promise<MasterItemDto>
  update: (id: string, body: MasterUpsertRequest) => Promise<MasterItemDto>
  showFlags?: boolean
  showSite?: boolean
}> = {
  departments: {
    title: 'Departments',
    list: (a) => mastersApi.departments(a),
    create: (b) => mastersApi.createDepartment(b),
    update: (id, b) => mastersApi.updateDepartment(id, b),
  },
  purposes: {
    title: 'Purposes',
    list: (a) => mastersApi.purposes(a),
    create: (b) => mastersApi.createPurpose(b),
    update: (id, b) => mastersApi.updatePurpose(id, b),
  },
  locations: {
    title: 'Locations',
    list: (a) => mastersApi.locations(a),
    create: (b) => mastersApi.createLocation(b),
    update: (id, b) => mastersApi.updateLocation(id, b),
    showFlags: true,
    showSite: true,
  },
}

const blank = (): MasterUpsertRequest => ({
  name: '',
  code: '',
  intercom: '',
  sortOrder: 0,
  isActive: true,
  requiresPlantNumber: false,
  requiresOtherText: false,
  isDefault: false,
  siteId: null,
})

export function MasterListPage({ kind }: { kind: MasterKind }) {
  const cfg = config[kind]
  const [items, setItems] = useState<MasterItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<MasterItemDto | null>(null)
  const [form, setForm] = useState<MasterUpsertRequest>(blank())
  const [sites, setSites] = useState<SiteDto[]>([])
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await cfg.list(false))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [cfg])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    if (!cfg.showSite) return
    void sitesApi.list(true).then(setSites).catch(() => setSites([]))
  }, [cfg.showSite])

  function startCreate() {
    setEditing(null)
    setForm(blank())
  }

  function startEdit(item: MasterItemDto) {
    setEditing(item)
    setForm({
      name: item.name,
      code: item.code || '',
      intercom: item.intercom || '',
      sortOrder: item.sortOrder,
      isActive: item.isActive,
      requiresPlantNumber: item.requiresPlantNumber,
      requiresOtherText: item.requiresOtherText,
      isDefault: item.isDefault,
      siteId: item.siteId ?? null,
    })
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setMessage(null)
    setError(null)
    try {
      if (editing) await cfg.update(editing.id, form)
      else await cfg.create(form)
      setMessage('Saved.')
      setEditing(null)
      setForm(blank())
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function deactivate(id: string) {
    setError(null)
    try {
      await mastersApi.deactivate(kind, id)
      setMessage('Deactivated.')
      await load()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  return (
    <div>
      <PageHeader
        title={cfg.title}
        actions={<Button variant="amber" onClick={startCreate}>Add new</Button>}
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <div className="grid gap-4 xl:grid-cols-[1fr_360px]">
        <Panel className="overflow-x-auto p-0">
          {loading ? <Spinner /> : null}
          {!loading && items.length === 0 ? <div className="p-4"><EmptyState title="No items" /></div> : null}
          {!loading && items.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Code</th>
                  {cfg.showSite ? <th className="px-4 py-3">Site</th> : null}
                  <th className="px-4 py-3">Active</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id} className="border-t border-gray-100">
                    <td className="px-4 py-3 font-medium">{item.name}</td>
                    <td className="px-4 py-3">{item.code || '—'}</td>
                    {cfg.showSite ? (
                      <td className="px-4 py-3">
                        {item.siteName ?? <span className="text-gray-500">All sites</span>}
                      </td>
                    ) : null}
                    <td className="px-4 py-3">{item.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                        {item.isActive ? (
                          <Button size="sm" variant="danger" onClick={() => void deactivate(item.id)}>Deactivate</Button>
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
          <h2 className="mb-3 font-semibold text-steel">{editing ? 'Edit item' : 'Create item'}</h2>
          <form onSubmit={save} className="space-y-3">
            <div>
              <FieldLabel>Name *</FieldLabel>
              <TextInput required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Code</FieldLabel>
              <TextInput value={form.code || ''} onChange={(e) => setForm({ ...form, code: e.target.value })} />
            </div>
            {kind === 'departments' ? (
              <div>
                <FieldLabel>Intercom</FieldLabel>
                <TextInput value={form.intercom || ''} onChange={(e) => setForm({ ...form, intercom: e.target.value })} />
              </div>
            ) : null}
            <div>
              <FieldLabel>Sort order</FieldLabel>
              <TextInput
                type="number"
                value={form.sortOrder}
                onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) })}
              />
            </div>
            <label className="flex min-h-11 items-center gap-2 text-sm">
              <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
              Active
            </label>
            {cfg.showSite ? (
              <div>
                <FieldLabel htmlFor="master-site">Site</FieldLabel>
                <TextSelect
                  id="master-site"
                  value={form.siteId || ''}
                  onChange={(e) => setForm({ ...form, siteId: e.target.value || null })}
                >
                  <option value="">Shared by all sites</option>
                  {sites.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </TextSelect>
              </div>
            ) : null}
            {cfg.showFlags ? (
              <>
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" checked={form.requiresPlantNumber} onChange={(e) => setForm({ ...form, requiresPlantNumber: e.target.checked })} />
                  Requires plant number
                </label>
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" checked={form.requiresOtherText} onChange={(e) => setForm({ ...form, requiresOtherText: e.target.checked })} />
                  Requires other text
                </label>
              </>
            ) : null}
            <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
          </form>
        </Panel>
      </div>
    </div>
  )
}

export function DepartmentsPage() {
  return <MasterListPage kind="departments" />
}
export function PurposesPage() {
  return <MasterListPage kind="purposes" />
}
export function LocationsPage() {
  return <MasterListPage kind="locations" />
}
