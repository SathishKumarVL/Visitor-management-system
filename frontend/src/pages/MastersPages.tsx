import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiErrorMessage, lookupsApi, mastersApi } from '../lib/api'
import type { LookupSourceOptionDto, MasterItemDto, MasterUpsertRequest, PurposeFieldDto } from '../types/api'
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
import { hasAnyRole } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

type MasterKind = 'departments' | 'purposes' | 'locations' | 'designations'

const singular: Record<MasterKind, string> = {
  departments: 'department',
  purposes: 'purpose',
  locations: 'location',
  designations: 'designation',
}

const config: Record<MasterKind, {
  title: string
  list: (activeOnly?: boolean, locationId?: string) => Promise<MasterItemDto[]>
  create: (body: MasterUpsertRequest) => Promise<MasterItemDto>
  update: (id: string, body: MasterUpsertRequest) => Promise<MasterItemDto>
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
  },
  designations: {
    title: 'Designations',
    list: (a) => mastersApi.designations(a),
    create: (b) => mastersApi.createDesignation(b),
    update: (id, b) => mastersApi.updateDesignation(id, b),
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

const MAX_PURPOSE_FIELDS = 5

export function MasterListPage({ kind }: { kind: MasterKind }) {
  const cfg = config[kind]
  const isSuperAdmin = hasAnyRole(useAuthStore((s) => s.user?.roles) ?? [], ['SuperAdmin'])
  const canAddAndRemove = kind !== 'locations' || isSuperAdmin
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

  const [lookupSources, setLookupSources] = useState<LookupSourceOptionDto[]>([])
  useEffect(() => {
    if (kind === 'purposes') void lookupsApi.sources().then(setLookupSources).catch(() => setLookupSources([]))
  }, [kind])
  const lookupName = (id?: string | null) => lookupSources.find((s) => s.id === id)?.name ?? 'lookup'

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
      fields: kind === 'purposes' ? [] : undefined,
      momRequired: kind === 'locations' ? true : undefined,
    })
    setFormOpen(true)
  }

  const fields = form.fields ?? []

  function setFields(next: PurposeFieldDto[]) {
    setForm((f) => ({ ...f, fields: next }))
  }

  function patchField(index: number, patch: Partial<PurposeFieldDto>) {
    setFields(fields.map((f, i) => (i === index ? { ...f, ...patch } : f)))
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
      fields: kind === 'purposes' ? (item.fields ?? []).map((f) => ({ ...f })) : undefined,
      momRequired: kind === 'locations' ? item.momRequired ?? true : undefined,
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
        fields: form.fields
          ?.map((f) => ({ ...f, label: f.label.trim() }))
          .filter((f) => f.label.length > 0),
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

  async function remove(item: MasterItemDto) {
    if (!window.confirm(`Delete ${singular[kind]} "${item.name}" permanently? This cannot be undone.`)) return
    setError(null)
    setMessage(null)
    try {
      await mastersApi.remove(kind, item.id)
      setMessage(`Deleted ${item.name}.`)
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
            {canAddAndRemove ? (
              <>
                <ExcelImportButtons kind={kind} label={cfg.title} onImported={load} />
                <Button variant="amber" onClick={startCreate}>Add new</Button>
              </>
            ) : null}
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
                    <td className="px-4 py-3 font-medium">
                      {item.name}
                      {item.fields?.length ? (
                        <div className="mt-0.5 text-xs font-normal text-ink-muted">
                          Extra fields:{' '}
                          {item.fields
                            .map(
                              (f) =>
                                `${f.label}${f.isRequired ? ' *' : ''}${f.type === 'lookup' ? ` (search ${lookupName(f.lookupSourceId)})` : ''}`,
                            )
                            .join(', ')}
                        </div>
                      ) : null}
                      {kind === 'locations' && item.momRequired === false ? (
                        <div className="mt-0.5 text-xs font-normal text-ink-muted">No minutes of meeting</div>
                      ) : null}
                    </td>
                    <td className="px-4 py-3">{item.code || '—'}</td>
                    {cfg.locationScoped ? (
                      <td className="px-4 py-3">{item.locationName || 'All locations'}</td>
                    ) : null}
                    <td className="px-4 py-3">{item.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                        {item.isActive && canAddAndRemove ? (
                          <Button size="sm" variant="danger" onClick={() => void deactivate(item.id)}>Deactivate</Button>
                        ) : null}
                        {isSuperAdmin ? (
                          <Button size="sm" variant="danger" onClick={() => void remove(item)}>Delete</Button>
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
        size="lg"
      >
          <form onSubmit={save} className="grid gap-3 sm:grid-cols-2">
            {formError ? <div className="sm:col-span-2"><Alert tone="error">{formError}</Alert></div> : null}
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
            {canAddAndRemove ? (
              <label className="flex min-h-11 items-center gap-2 self-end text-sm">
                <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
                Active
              </label>
            ) : null}
            {kind === 'locations' ? (
              <div className="sm:col-span-2">
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={form.momRequired ?? true}
                    onChange={(e) => setForm({ ...form, momRequired: e.target.checked })}
                  />
                  Minutes of meeting (MoM)
                </label>
                <p className="text-xs text-ink-muted">
                  {form.momRequired ?? true
                    ? 'Hosts are reminded to add minutes after each visit here, and visits stay "MoM pending" until they do.'
                    : 'Visits here are marked "MoM not required": no minutes reminder emails, and nothing shows as pending.'}
                </p>
              </div>
            ) : null}
            {kind === 'purposes' ? (
              <div className="rounded-xl border border-border bg-mint/40 p-3 sm:col-span-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p className="text-sm font-semibold text-ink">Extra fields</p>
                    <p className="text-xs text-ink-muted">
                      Asked at walk-in registration only when this purpose is selected, e.g. Invoice number.
                      {lookupSources.length > 0 ? (
                        ' A "Search" field looks the value up in an outside system.'
                      ) : isSuperAdmin ? (
                        <>
                          {' '}To search an outside system (e.g. a student list),{' '}
                          <Link to="/settings?tab=lookups" className="font-medium text-primary underline">
                            set up a lookup source
                          </Link>{' '}
                          first; it then appears in the field&apos;s type list.
                        </>
                      ) : (
                        ' Super Admin can connect outside systems to search in Settings, under Lookup sources.'
                      )}
                    </p>
                  </div>
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    disabled={fields.length >= MAX_PURPOSE_FIELDS}
                    onClick={() => setFields([...fields, { id: null, label: '', isRequired: false }])}
                  >
                    + Add field
                  </Button>
                </div>
                {fields.length === 0 ? (
                  <p className="mt-2 text-sm text-ink-muted">No extra fields.</p>
                ) : (
                  <div className="mt-3 space-y-2">
                    {fields.map((field, index) => (
                      <div key={field.id || `new-${index}`} className="flex flex-wrap items-center gap-2">
                        <TextInput
                          className="min-w-0 flex-1 basis-56"
                          maxLength={100}
                          placeholder="Field name, e.g. Vehicle number"
                          aria-label={`Extra field ${index + 1} name`}
                          value={field.label}
                          onChange={(e) => patchField(index, { label: e.target.value })}
                        />
                        <TextSelect
                          className="basis-44"
                          aria-label={`Extra field ${index + 1} type`}
                          value={field.type === 'lookup' ? field.lookupSourceId ?? '' : ''}
                          onChange={(e) =>
                            patchField(
                              index,
                              e.target.value
                                ? { type: 'lookup', lookupSourceId: e.target.value }
                                : { type: 'text', lookupSourceId: null },
                            )
                          }
                        >
                          <option value="">Text</option>
                          {lookupSources.map((s) => (
                            <option key={s.id} value={s.id}>
                              Search {s.name}
                              {s.isActive ? '' : ' (off)'}
                            </option>
                          ))}
                        </TextSelect>
                        <label className="flex min-h-11 items-center gap-2 text-sm">
                          <input
                            type="checkbox"
                            checked={field.isRequired}
                            onChange={(e) => patchField(index, { isRequired: e.target.checked })}
                          />
                          Required
                        </label>
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          aria-label={`Remove ${field.label || `field ${index + 1}`}`}
                          onClick={() => setFields(fields.filter((_, i) => i !== index))}
                        >
                          Remove
                        </Button>
                      </div>
                    ))}
                  </div>
                )}
                {fields.length >= MAX_PURPOSE_FIELDS ? (
                  <p className="mt-2 text-xs text-ink-muted">A purpose can have up to {MAX_PURPOSE_FIELDS} extra fields.</p>
                ) : null}
              </div>
            ) : null}
            <div className="flex justify-end gap-2 border-t border-gray-100 pt-3 sm:col-span-2">
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
export function DesignationsPage() {
  return <MasterListPage kind="designations" />
}
