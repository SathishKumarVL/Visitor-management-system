import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { MasterItemDto, MeetingHallDto, MeetingHallUpsertRequest } from '../types/api'
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
import { useAuthStore } from '../store/authStore'
import { hasAnyRole } from '../lib/utils'

const blank = (locationId: string): MeetingHallUpsertRequest => ({
  code: '',
  name: '',
  seatingCapacity: 1,
  intercomNo: '',
  locationId: locationId || null,
  isActive: true,
})

export function MeetingHallsPage() {
  const [items, setItems] = useState<MeetingHallDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [filterLocationId, setFilterLocationId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<MeetingHallDto | null>(null)
  const [form, setForm] = useState<MeetingHallUpsertRequest>(blank(''))
  const [saving, setSaving] = useState(false)
  const [formOpen, setFormOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  // Users bound to a location only see and manage that location's halls (the API enforces this too).
  const userLocationId = useAuthStore((s) => s.user?.locationId) || ''
  const isSuperAdmin = hasAnyRole(useAuthStore((s) => s.user?.roles) ?? [], ['SuperAdmin'])
  const effectiveLocationId = userLocationId || filterLocationId
  const visibleLocations = userLocationId ? locations.filter((l) => l.id === userLocationId) : locations

  const { sorted, sort, onSort } = useSort(items, {
    code: (h) => h.code,
    name: (h) => h.name,
    capacity: (h) => h.seatingCapacity,
    intercom: (h) => h.intercomNo,
    location: (h) => h.locationName,
    status: (h) => (h.occupiedByVisitNumber ? 1 : 0),
    active: (h) => (h.isActive ? 0 : 1),
  })
  const { pageItems, pagination } = usePagination(sorted, 20, `${effectiveLocationId}|${sort?.key}|${sort?.dir}`)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await mastersApi.meetingHalls(false, effectiveLocationId || undefined))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [effectiveLocationId])

  useEffect(() => {
    void mastersApi.locations(true).then(setLocations).catch(() => setLocations([]))
  }, [])

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
    setForm(blank(effectiveLocationId))
    setFormOpen(true)
  }

  function startEdit(item: MeetingHallDto) {
    setEditing(item)
    setFormError(null)
    setMessage(null)
    setForm({
      code: item.code,
      name: item.name,
      seatingCapacity: item.seatingCapacity,
      intercomNo: item.intercomNo || '',
      locationId: item.locationId,
      isActive: item.isActive,
    })
    setFormOpen(true)
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    if (!form.locationId) {
      setFormError('Select a location for this meeting hall.')
      return
    }
    if (!Number.isInteger(form.seatingCapacity) || form.seatingCapacity < 1) {
      setFormError('Seating capacity must be a whole number of at least 1.')
      return
    }
    setSaving(true)
    setMessage(null)
    setFormError(null)
    try {
      const body: MeetingHallUpsertRequest = {
        ...form,
        code: form.code.trim(),
        name: form.name.trim(),
        intercomNo: form.intercomNo?.trim() || null,
      }
      if (editing) await mastersApi.updateMeetingHall(editing.id, body)
      else await mastersApi.createMeetingHall(body)
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
      await mastersApi.deactivate('meeting-halls', id)
      setMessage('Deactivated.')
      await load()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  async function remove(item: MeetingHallDto) {
    if (!window.confirm(`Delete meeting hall "${item.name}" permanently? This cannot be undone.`)) return
    setError(null)
    setMessage(null)
    try {
      await mastersApi.remove('meeting-halls', item.id)
      setMessage(`Deleted ${item.name}.`)
      await load()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  return (
    <div>
      {saving ? <LoadingMask label="Saving…" /> : null}
      {loading ? <Spinner label="Loading meeting halls…" /> : null}
      <PageHeader
        title="Meeting Halls"
        subtitle="At check-in each visitor is given the smallest free hall at their location that seats the whole party. Halls with 6 or more seats are kept for groups of 6 or more. A hall is free again once the visitor checks out."
        actions={
          <div className="flex flex-wrap gap-2">
            <TextSelect
              value={effectiveLocationId}
              disabled={!!userLocationId}
              onChange={(e) => setFilterLocationId(e.target.value)}
              className="w-full min-w-[12rem] sm:w-56"
            >
              {userLocationId ? null : <option value="">All locations</option>}
              {visibleLocations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </TextSelect>
            <ExcelImportButtons kind="meeting-halls" label="Meeting Halls" onImported={load} />
            <Button variant="amber" onClick={startCreate}>Add new</Button>
          </div>
        }
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <Panel className="overflow-x-auto p-0">
        {!loading && items.length === 0 ? <div className="p-4"><EmptyState title="No meeting halls" /></div> : null}
        {!loading && items.length > 0 ? (
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr>
                <SortHeader sortKey="code" sort={sort} onSort={onSort}>Code</SortHeader>
                <SortHeader sortKey="name" sort={sort} onSort={onSort}>Name</SortHeader>
                <SortHeader sortKey="capacity" sort={sort} onSort={onSort}>Seating capacity</SortHeader>
                <SortHeader sortKey="intercom" sort={sort} onSort={onSort}>Intercom no</SortHeader>
                <SortHeader sortKey="location" sort={sort} onSort={onSort}>Location</SortHeader>
                <SortHeader sortKey="status" sort={sort} onSort={onSort}>Availability</SortHeader>
                <SortHeader sortKey="active" sort={sort} onSort={onSort}>Active</SortHeader>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {pageItems.map((item) => (
                <tr key={item.id} className="border-t border-gray-100">
                  <td className="px-4 py-3 font-medium">{item.code}</td>
                  <td className="px-4 py-3">{item.name}</td>
                  <td className="px-4 py-3">{item.seatingCapacity}</td>
                  <td className="px-4 py-3">{item.intercomNo || '—'}</td>
                  <td className="px-4 py-3">{item.locationName}</td>
                  <td className="px-4 py-3">
                    {item.occupiedByVisitNumber ? (
                      <span className="text-amber-700" title={item.occupiedByVisitNumber}>
                        In use · {item.occupiedByVisitorName}
                      </span>
                    ) : item.isActive ? (
                      <span className="text-emerald-700">Free</span>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td className="px-4 py-3">{item.isActive ? 'Yes' : 'No'}</td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex justify-end gap-2">
                      <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                      {item.isActive ? (
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
        title={`${editing ? 'Edit' : 'Add'} meeting hall`}
        subtitle={editing ? editing.name : undefined}
        onClose={closeForm}
        size="lg"
      >
        <form onSubmit={save} className="space-y-3">
          {formError ? <Alert tone="error">{formError}</Alert> : null}
          <div className="grid gap-3 sm:grid-cols-2">
            <div>
              <FieldLabel>Code *</FieldLabel>
              <TextInput required maxLength={50} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Name *</FieldLabel>
              <TextInput required maxLength={150} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Seating capacity *</FieldLabel>
              <TextInput
                required
                type="number"
                min={1}
                step={1}
                value={form.seatingCapacity}
                onChange={(e) => setForm({ ...form, seatingCapacity: Number(e.target.value) })}
              />
            </div>
            <div>
              <FieldLabel>Intercom no</FieldLabel>
              <TextInput maxLength={50} value={form.intercomNo || ''} onChange={(e) => setForm({ ...form, intercomNo: e.target.value })} />
            </div>
          <div>
            <FieldLabel>Location *</FieldLabel>
            <TextSelect
              required
              value={form.locationId || ''}
              disabled={!!userLocationId}
              onChange={(e) => setForm({ ...form, locationId: e.target.value || null })}
            >
              {userLocationId ? null : <option value="">Select location</option>}
              {visibleLocations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </TextSelect>
          </div>
          <label className="flex min-h-11 items-center gap-2 self-end text-sm">
            <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
            Active
          </label>
          </div>
          <div className="flex justify-end gap-2 border-t border-gray-100 pt-3">
            <Button type="button" variant="secondary" onClick={closeForm}>Cancel</Button>
            <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
          </div>
        </form>
      </Modal>
    </div>
  )
}
