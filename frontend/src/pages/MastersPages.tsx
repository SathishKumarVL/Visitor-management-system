import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { MasterItemDto, MasterUpsertRequest } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { ExcelImportButtons } from '../components/ExcelImportButtons'
import { Modal } from '../components/ui/Modal'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'
import { useSort } from '../lib/useSort'
import { SortHeader } from '../components/ui/SortHeader'

type MasterKind = 'departments' | 'purposes' | 'locations'

const singular: Record<MasterKind, string> = {
  departments: 'department',
  purposes: 'purpose',
  locations: 'location',
}

const config: Record<MasterKind, {
  title: string
  list: (activeOnly?: boolean, locationId?: string) => Promise<MasterItemDto[]>
  create: (body: MasterUpsertRequest) => Promise<MasterItemDto>
  update: (id: string, body: MasterUpsertRequest) => Promise<MasterItemDto>
  showFlags?: boolean
  locationScoped?: boolean
}> = {
  departments: {
    title: 'Departments',
    list: (a, loc) => mastersApi.departments(a, loc),
    create: (b) => mastersApi.createDepartment(b),
    update: (id, b) => mastersApi.updateDepartment(id, b),
    locationScoped: true,
  },
  purposes: {
    title: 'Purposes',
    list: (a, loc) => mastersApi.purposes(a, loc),
    create: (b) => mastersApi.createPurpose(b),
    update: (id, b) => mastersApi.updatePurpose(id, b),
    locationScoped: true,
  },
  locations: {
    title: 'Locations',
    list: (a) => mastersApi.locations(a),
    create: (b) => mastersApi.createLocation(b),
    update: (id, b) => mastersApi.updateLocation(id, b),
    showFlags: true,
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
  locationId: null,
})

export function MasterListPage({ kind }: { kind: MasterKind }) {
  const cfg = config[kind]
  const [items, setItems] = useState<MasterItemDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [filterLocationId, setFilterLocationId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<MasterItemDto | null>(null)
  const [form, setForm] = useState<MasterUpsertRequest>(blank())
  const [saving, setSaving] = useState(false)
  const [formOpen, setFormOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const { sorted, sort, onSort } = useSort(items, {
    name: (i) => i.name,
    code: (i) => i.code,
    location: (i) => i.locationName,
    active: (i) => (i.isActive ? 0 : 1),
  })
  const { pageItems, pagination } = usePagination(sorted, 20, `${kind}|${filterLocationId}|${sort?.key}|${sort?.dir}`)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await cfg.list(false, cfg.locationScoped ? filterLocationId || undefined : undefined))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [cfg, filterLocationId])

  useEffect(() => {
    if (cfg.locationScoped) {
      void mastersApi.locations(true).then(setLocations).catch(() => setLocations([]))
    }
  }, [cfg.locationScoped])

  useEffect(() => {
    void load()
  }, [load])

  function closeForm() {
    setFormOpen(false)
    setEditing(null)
    setFormError(null)
  }

  function startCreate() {
    setEditing(null)
    setFormError(null)
    setMessage(null)
    setForm({
      ...blank(),
      locationId: cfg.locationScoped ? filterLocationId || null : null,
    })
    setFormOpen(true)
  }

  function startEdit(item: MasterItemDto) {
    setEditing(item)
    setFormError(null)
    setMessage(null)
    setFormOpen(true)
    setForm({
      name: item.name,
      code: item.code || '',
      intercom: item.intercom || '',
      sortOrder: item.sortOrder,
      isActive: item.isActive,
      requiresPlantNumber: item.requiresPlantNumber,
      requiresOtherText: item.requiresOtherText,
      isDefault: item.isDefault,
      locationId: item.locationId || null,
    })
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    if (kind === 'purposes' && !form.locationId) {
      setFormError('Select a location for this purpose.')
      return
    }
    setSaving(true)
    setMessage(null)
    setFormError(null)
    try {
      const body: MasterUpsertRequest = {
        ...form,
        locationId: form.locationId || null,
      }
      if (editing) await cfg.update(editing.id, body)
      else await cfg.create(body)
      setMessage('Saved.')
      closeForm()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
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
      {saving ? <LoadingMask label="Saving…" /> : null}
      {loading ? <Spinner label={`Loading ${cfg.title.toLowerCase()}…`} /> : null}
      <PageHeader
        title={cfg.title}
        actions={
          <div className="flex flex-wrap gap-2">
            {cfg.locationScoped ? (
              <TextSelect
                value={filterLocationId}
                onChange={(e) => setFilterLocationId(e.target.value)}
                className="w-full min-w-[12rem] sm:w-56"
              >
                <option value="">All locations</option>
                {locations.map((l) => (
                  <option key={l.id} value={l.id}>{l.name}</option>
                ))}
              </TextSelect>
            ) : null}
            <ExcelImportButtons kind={kind} label={cfg.title} onImported={load} />
            <Button variant="amber" onClick={startCreate}>Add new</Button>
          </div>
        }
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <Panel className="overflow-x-auto p-0">
          {!loading && items.length === 0 ? <div className="p-4"><EmptyState title="No items" /></div> : null}
          {!loading && items.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <SortHeader sortKey="name" sort={sort} onSort={onSort}>Name</SortHeader>
                  <SortHeader sortKey="code" sort={sort} onSort={onSort}>Code</SortHeader>
                  {cfg.locationScoped ? <SortHeader sortKey="location" sort={sort} onSort={onSort}>Location</SortHeader> : null}
                  <SortHeader sortKey="active" sort={sort} onSort={onSort}>Active</SortHeader>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {pageItems.map((item) => (
                  <tr key={item.id} className="border-t border-gray-100">
                    <td className="px-4 py-3 font-medium">{item.name}</td>
                    <td className="px-4 py-3">{item.code || '—'}</td>
                    {cfg.locationScoped ? (
                      <td className="px-4 py-3">{item.locationName || 'All locations'}</td>
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
          {!loading ? <Pagination {...pagination} /> : null}
      </Panel>

      <Modal
        open={formOpen}
        title={`${editing ? 'Edit' : 'Add'} ${singular[kind]}`}
        subtitle={editing ? editing.name : undefined}
        onClose={closeForm}
      >
          <form onSubmit={save} className="space-y-3">
            {formError ? <Alert tone="error">{formError}</Alert> : null}
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
            {cfg.locationScoped ? (
              <div>
                <FieldLabel>Location{kind === 'purposes' ? ' *' : ''}</FieldLabel>
                <TextSelect
                  required={kind === 'purposes'}
                  value={form.locationId || ''}
                  onChange={(e) => setForm({ ...form, locationId: e.target.value || null })}
                >
                  <option value="">{kind === 'purposes' ? 'Select location' : 'All locations'}</option>
                  {locations.map((l) => (
                    <option key={l.id} value={l.id}>{l.name}</option>
                  ))}
                </TextSelect>
                <p className="mt-1 text-xs text-ink-muted">
                  {kind === 'purposes'
                    ? 'Purposes are tied to a location and only offered when that location is selected at registration.'
                    : 'Leave blank to show this item for every location.'}
                </p>
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
            <div className="flex justify-end gap-2 border-t border-gray-100 pt-3">
              <Button type="button" variant="secondary" onClick={closeForm}>Cancel</Button>
              <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
            </div>
          </form>
      </Modal>
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
