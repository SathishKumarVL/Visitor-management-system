import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { CreateEmployeeRequest, EmployeeDto, MasterItemDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { ExcelImportButtons } from '../components/ExcelImportButtons'
import { EmployeeLoginModal } from '../components/EmployeeLoginModal'
import { Modal } from '../components/ui/Modal'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'
import { useSort } from '../lib/useSort'
import { SortHeader } from '../components/ui/SortHeader'
import { hasAnyRole, sanitizePhoneDigits, validateOptionalTenDigitPhone } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

const blank = (departmentId = '', locationId: string | null = null): CreateEmployeeRequest => ({
  fullName: '',
  code: '',
  email: '',
  phone: '',
  intercom: '',
  designation: '',
  departmentId,
  locationId,
  isActive: true,
})

export function EmployeesPage() {
  const [items, setItems] = useState<EmployeeDto[]>([])
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [departmentId, setDepartmentId] = useState('')
  const [locationId, setLocationId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<EmployeeDto | null>(null)
  const [form, setForm] = useState<CreateEmployeeRequest>(blank())
  const [saving, setSaving] = useState(false)
  const [formOpen, setFormOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [loginFor, setLoginFor] = useState<EmployeeDto | null>(null)
  const canCreateLogin = hasAnyRole(useAuthStore((s) => s.user?.roles) ?? [], ['SuperAdmin'])
  // Users bound to a location only see and manage that location's employees (the API enforces this too).
  const userLocationId = useAuthStore((s) => s.user?.locationId) || ''
  const effectiveLocationId = userLocationId || locationId
  const visibleLocations = userLocationId ? locations.filter((l) => l.id === userLocationId) : locations
  const { sorted, sort, onSort } = useSort(items, {
    code: (e) => e.code,
    name: (e) => e.fullName,
    designation: (e) => e.designation,
    department: (e) => e.departmentName,
    location: (e) => e.locationName,
    status: (e) => (e.isActive ? 0 : 1),
  })
  const { pageItems, pagination } = usePagination(sorted, 20, `${departmentId}|${effectiveLocationId}|${sort?.key}|${sort?.dir}`)

  const formDepartments = useMemo(() => {
    const loc = form.locationId
    if (!loc) return departments
    return departments.filter((d) => !d.locationId || d.locationId === loc)
  }, [departments, form.locationId])

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await mastersApi.hosts(departmentId || undefined, false, effectiveLocationId || undefined))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [departmentId, effectiveLocationId])

  useEffect(() => {
    void Promise.all([
      mastersApi.departments(false),
      mastersApi.locations(true),
    ]).then(([d, l]) => {
      setDepartments(d)
      setLocations(l)
    }).catch((e) => setError(apiErrorMessage(e)))
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
    const loc = effectiveLocationId || null
    const depts = loc
      ? departments.filter((d) => !d.locationId || d.locationId === loc)
      : departments
    setForm(blank(departmentId || depts[0]?.id || '', loc))
    setFormOpen(true)
  }

  function startEdit(item: EmployeeDto) {
    setEditing(item)
    setFormError(null)
    setMessage(null)
    setFormOpen(true)
    setForm({
      fullName: item.fullName,
      code: item.code || '',
      email: item.email || '',
      phone: item.phone || '',
      intercom: item.intercom || '',
      designation: item.designation || '',
      departmentId: item.departmentId,
      locationId: item.locationId || null,
      userId: item.userId,
      isActive: item.isActive,
    })
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    const phoneCheck = validateOptionalTenDigitPhone(form.phone || '')
    if (!phoneCheck.ok) {
      setFormError(phoneCheck.error)
      return
    }
    setSaving(true)
    setMessage(null)
    setFormError(null)
    try {
      const body: CreateEmployeeRequest = {
        ...form,
        phone: phoneCheck.phone,
        locationId: form.locationId || null,
      }
      if (editing) await mastersApi.updateHost(editing.id, body)
      else await mastersApi.createHost(body)
      setMessage(editing ? 'Employee updated.' : 'Employee added.')
      closeForm()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function deactivate(item: EmployeeDto) {
    setSaving(true)
    setError(null)
    try {
      await mastersApi.deactivateHost(item.id)
      setMessage('Employee deactivated.')
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      {saving ? <LoadingMask label="Saving…" /> : null}
      {loading ? <Spinner label="Loading employees…" /> : null}
      <PageHeader
        title="Employees"
        actions={
          <div className="flex flex-wrap gap-2">
            <TextSelect
              value={effectiveLocationId}
              disabled={!!userLocationId}
              onChange={(e) => setLocationId(e.target.value)}
              className="w-full min-w-[12rem] sm:w-48"
            >
              {userLocationId ? null : <option value="">All locations</option>}
              {visibleLocations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </TextSelect>
            <TextSelect value={departmentId} onChange={(e) => setDepartmentId(e.target.value)} className="w-full min-w-[12rem] sm:w-48">
              <option value="">All departments</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>{d.name}</option>
              ))}
            </TextSelect>
            <ExcelImportButtons kind="employees" label="Employees" onImported={load} />
            <Button variant="amber" onClick={startCreate}>Add employee</Button>
          </div>
        }
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <Panel className="overflow-x-auto p-0">
          {!loading && items.length === 0 ? <div className="p-4"><EmptyState title="No employees" /></div> : null}
          {!loading && items.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <SortHeader sortKey="code" sort={sort} onSort={onSort}>Employee code</SortHeader>
                  <SortHeader sortKey="name" sort={sort} onSort={onSort}>Name</SortHeader>
                  <SortHeader sortKey="designation" sort={sort} onSort={onSort}>Designation</SortHeader>
                  <SortHeader sortKey="department" sort={sort} onSort={onSort}>Department</SortHeader>
                  <SortHeader sortKey="location" sort={sort} onSort={onSort}>Location</SortHeader>
                  <th className="px-4 py-3">Phone</th>
                  <th className="px-4 py-3">Email</th>
                  <SortHeader sortKey="status" sort={sort} onSort={onSort}>Status</SortHeader>
                  <th className="px-4 py-3">Login</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {pageItems.map((item) => (
                  <tr key={item.id} className="border-t border-gray-100">
                    <td className="px-4 py-3">{item.code || '—'}</td>
                    <td className="px-4 py-3 font-medium">{item.fullName}</td>
                    <td className="px-4 py-3">{item.designation || '—'}</td>
                    <td className="px-4 py-3">{item.departmentName}</td>
                    <td className="px-4 py-3">{item.locationName || 'All locations'}</td>
                    <td className="px-4 py-3">{item.phone || '—'}</td>
                    <td className="px-4 py-3">{item.email || '—'}</td>
                    <td className="px-4 py-3">{item.isActive ? 'Active' : 'Inactive'}</td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      {item.userName ? (
                        <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700 ring-1 ring-emerald-200">
                          {item.userName}
                        </span>
                      ) : (
                        <span className="text-ink-muted">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-right whitespace-nowrap">
                      {canCreateLogin && item.isActive && !item.userName ? (
                        <Button size="sm" variant="secondary" className="mr-2" onClick={() => setLoginFor(item)}>
                          Create login
                        </Button>
                      ) : null}
                      <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                      {item.isActive ? (
                        <Button size="sm" variant="danger" className="ml-2" onClick={() => void deactivate(item)}>
                          Deactivate
                        </Button>
                      ) : null}
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
        title={editing ? 'Edit employee' : 'Add employee'}
        subtitle={editing ? editing.fullName : undefined}
        onClose={closeForm}
        size="lg"
      >
          <form onSubmit={save} className="grid gap-3 sm:grid-cols-2">
            {formError ? <div className="sm:col-span-2"><Alert tone="error">{formError}</Alert></div> : null}
            <div>
              <FieldLabel>Employee code</FieldLabel>
              <TextInput value={form.code || ''} onChange={(e) => setForm({ ...form, code: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Employee name *</FieldLabel>
              <TextInput required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Designation</FieldLabel>
              <TextInput value={form.designation || ''} onChange={(e) => setForm({ ...form, designation: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Location</FieldLabel>
              <TextSelect
                value={form.locationId || ''}
                onChange={(e) => {
                  const nextLoc = e.target.value || null
                  const nextDepts = nextLoc
                    ? departments.filter((d) => !d.locationId || d.locationId === nextLoc)
                    : departments
                  const keepDept = nextDepts.some((d) => d.id === form.departmentId)
                  setForm({
                    ...form,
                    locationId: nextLoc,
                    departmentId: keepDept ? form.departmentId : (nextDepts[0]?.id || ''),
                  })
                }}
              >
                {userLocationId && !(editing && !editing.locationId) ? null : <option value="">All locations</option>}
                {visibleLocations.map((l) => (
                  <option key={l.id} value={l.id}>{l.name}</option>
                ))}
              </TextSelect>
            </div>
            <div>
              <FieldLabel>Department *</FieldLabel>
              <TextSelect required value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })}>
                <option value="">Select</option>
                {formDepartments.map((d) => (
                  <option key={d.id} value={d.id}>{d.name}</option>
                ))}
              </TextSelect>
            </div>
            <div>
              <FieldLabel>Phone number</FieldLabel>
              <TextInput
                inputMode="numeric"
                autoComplete="tel-national"
                placeholder="10-digit number"
                maxLength={10}
                value={form.phone || ''}
                onChange={(e) => setForm({ ...form, phone: sanitizePhoneDigits(e.target.value) })}
              />
            </div>
            <div>
              <FieldLabel>Email address</FieldLabel>
              <TextInput type="email" value={form.email || ''} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            </div>
            <label className="flex items-center gap-2 text-sm sm:col-span-2">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              />
              Active
            </label>
            <div className="flex justify-end gap-2 border-t border-gray-100 pt-3 sm:col-span-2">
              <Button type="button" variant="secondary" onClick={closeForm}>Cancel</Button>
              <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
            </div>
          </form>
      </Modal>

      {loginFor ? (
        <EmployeeLoginModal
          employee={loginFor}
          onClose={() => setLoginFor(null)}
          onCreated={async (updated) => {
            setMessage(`Login '${updated.userName}' created for ${updated.fullName}.`)
            await load()
          }}
        />
      ) : null}
    </div>
  )
}

/** @deprecated Prefer EmployeesPage — same master data used as people to meet. */
export const HostsPage = EmployeesPage
