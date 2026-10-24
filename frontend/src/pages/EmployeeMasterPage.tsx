import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { apiErrorMessage, employeeMasterApi, mastersApi, usersApi } from '../lib/api'
import type { EmployeeDto, EmployeeGender, EmployeeProfileRequest, EmployeeTitle, MasterItemDto, UserDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextInput, TextSelect } from '../components/ui/Field'
import { Modal } from '../components/ui/Modal'
import { Pagination } from '../components/ui/Pagination'
import { SortHeader } from '../components/ui/SortHeader'
import { FaceCapture } from '../components/FaceCapture'
import { SecureImage } from '../components/SecureImage'
import { EmployeeLoginModal } from '../components/EmployeeLoginModal'
import { ManageLoginModal } from '../components/ManageLoginModal'
import {
  ClearAllMasterButton,
  DeleteSelectedMasterButton,
  SelectPageHeader,
  SelectRowCell,
} from '../components/ClearAllMasterButton'
import { useRowSelection } from '../lib/useRowSelection'
import { roleLabel } from '../lib/roles'
import { usePagination } from '../lib/usePagination'
import { useSort } from '../lib/useSort'
import { formatDate, hasAnyRole, sanitizePhoneDigits, titledName } from '../lib/utils'
import { ExcelImportButtons } from '../components/ExcelImportButtons'
import { useAuthStore } from '../store/authStore'

const GENDERS: { value: EmployeeGender; label: string }[] = [
  { value: 1, label: 'Male' },
  { value: 2, label: 'Female' },
  { value: 3, label: 'Other' },
]

function genderLabel(value?: EmployeeGender | null) {
  return GENDERS.find((g) => g.value === value)?.label ?? '—'
}

const today = () => new Date().toISOString().slice(0, 10)

const TITLES: { value: EmployeeTitle; gender: EmployeeGender }[] = [
  { value: 'Mr', gender: 1 },
  { value: 'Ms', gender: 2 },
]

const blank = (departmentId = '', locationId: string | null = null): EmployeeProfileRequest => ({
  title: null,
  fullName: '',
  code: '',
  dateOfBirth: '',
  gender: null,
  phone: '',
  email: '',
  designation: '',
  intercom: '',
  departmentId,
  locationId,
  isActive: true,
  facePhotoBase64: null,
})

/**
 * Employee menu: the employee master with date of birth, sex and face, and the logins created from it. The face
 * recognises them as a group employee at any location and becomes their face sign-in.
 */
export function EmployeeMasterPage() {
  const [items, setItems] = useState<EmployeeDto[]>([])
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [designations, setDesignations] = useState<MasterItemDto[]>([])
  const [departmentId, setDepartmentId] = useState('')
  const [locationId, setLocationId] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<EmployeeDto | null>(null)
  const [form, setForm] = useState<EmployeeProfileRequest>(blank())
  const [formError, setFormError] = useState<string | null>(null)
  const [retakeFace, setRetakeFace] = useState(false)
  const [loginFor, setLoginFor] = useState<EmployeeDto | null>(null)
  const [users, setUsers] = useState<UserDto[]>([])
  const [managing, setManaging] = useState<{ user: UserDto; employee: EmployeeDto | null } | null>(null)
  const usersById = useMemo(() => new Map(users.map((u) => [u.id, u])), [users])
  const sharedLogins = useMemo(() => users.filter((u) => !u.employeeId), [users])

  // Admins are bound to their location; the API enforces this as well.
  const userLocationId = useAuthStore((s) => s.user?.locationId) || ''
  const isSuperAdmin = hasAnyRole(useAuthStore((s) => s.user?.roles) ?? [], ['SuperAdmin'])
  const effectiveLocationId = userLocationId || locationId
  const visibleLocations = userLocationId ? locations.filter((l) => l.id === userLocationId) : locations

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return items
    return items.filter((e) =>
      [e.fullName, e.code, e.phone, e.email, e.designation].some((v) => v?.toLowerCase().includes(term)),
    )
  }, [items, search])

  const { sorted, sort, onSort } = useSort(filtered, {
    name: (e) => e.fullName,
    dob: (e) => e.dateOfBirth,
    department: (e) => e.departmentName,
    login: (e) => e.userName,
    status: (e) => (e.isActive ? 0 : 1),
  })
  const { pageItems, pagination } = usePagination(
    sorted,
    20,
    `${departmentId}|${effectiveLocationId}|${search}|${sort?.key}|${sort?.dir}`,
  )
  const selection = useRowSelection(sorted.map((e) => e.id))

  const formDepartments = useMemo(() => {
    const loc = form.locationId
    if (!loc) return departments
    return departments.filter((d) => !d.locationId || d.locationId === loc)
  }, [departments, form.locationId])

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [employees, logins] = await Promise.all([
        employeeMasterApi.list(effectiveLocationId || undefined, departmentId || undefined),
        usersApi.list().catch(() => [] as UserDto[]),
      ])
      setItems(employees)
      setUsers(logins)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [departmentId, effectiveLocationId])

  useEffect(() => {
    void Promise.all([mastersApi.departments(true), mastersApi.locations(true), mastersApi.designations(true)])
      .then(([d, l, g]) => {
        setDepartments(d)
        setLocations(l)
        setDesignations(g)
      })
      .catch((e) => setError(apiErrorMessage(e)))
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function closeForm() {
    setFormOpen(false)
    setEditing(null)
    setFormError(null)
    setRetakeFace(false)
  }

  function startCreate() {
    const loc = effectiveLocationId || null
    const depts = loc ? departments.filter((d) => !d.locationId || d.locationId === loc) : departments
    setEditing(null)
    setForm(blank(departmentId || depts[0]?.id || '', loc))
    setFormError(null)
    setMessage(null)
    setRetakeFace(false)
    setFormOpen(true)
  }

  function startEdit(item: EmployeeDto) {
    setEditing(item)
    setForm({
      title: item.title ?? null,
      fullName: item.fullName,
      code: item.code || '',
      dateOfBirth: item.dateOfBirth || '',
      gender: item.gender ?? null,
      phone: item.phone || '',
      email: item.email || '',
      designation: item.designation || '',
      intercom: item.intercom || '',
      departmentId: item.departmentId,
      locationId: item.locationId || null,
      isActive: item.isActive,
      facePhotoBase64: null,
    })
    setFormError(null)
    setMessage(null)
    setRetakeFace(!item.hasFace)
    setFormOpen(true)
  }

  function validate(): string | null {
    if (!form.code.trim()) return 'Enter the employee ID.'
    if (!form.title) return 'Select Mr or Ms.'
    if (!form.fullName.trim()) return 'Enter the name.'
    if (!form.dateOfBirth) return 'Enter the date of birth.'
    if (form.dateOfBirth > today()) return 'Date of birth cannot be in the future.'
    if (!form.gender) return 'Select the sex.'
    if (form.phone.length !== 10) return 'Mobile number must be exactly 10 digits.'
    if (!form.email.trim()) return 'Enter the email address.'
    if (!form.departmentId) return 'Select the department.'
    const needsFace = !editing || !editing.hasFace || retakeFace
    if (needsFace && !form.facePhotoBase64) return "Capture the employee's face."
    return null
  }

  async function persist(isActive: boolean, done: string) {
    const problem = validate()
    if (problem) {
      setFormError(problem)
      return
    }
    setSaving(true)
    setFormError(null)
    try {
      const body: EmployeeProfileRequest = {
        ...form,
        code: form.code.trim(),
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        locationId: form.locationId || null,
        facePhotoBase64: form.facePhotoBase64 || null,
        isActive,
      }
      if (editing) await employeeMasterApi.update(editing.id, body)
      else await employeeMasterApi.create(body)
      setMessage(done)
      closeForm()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  function save(e: FormEvent) {
    e.preventDefault()
    void persist(editing ? editing.isActive : true, editing ? 'Employee updated.' : 'Employee added.')
  }

  async function deactivate(item: EmployeeDto) {
    if (!window.confirm(`Deactivate ${titledName(item)}? Reception will no longer recognise them as an employee.`)) return
    setSaving(true)
    setFormError(null)
    try {
      await employeeMasterApi.deactivate(item.id)
      setMessage(`${item.fullName} deactivated.`)
      closeForm()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function remove(item: EmployeeDto) {
    if (!window.confirm(`Delete ${titledName(item)} permanently? This cannot be undone. Their login, if any, is kept.`)) return
    setSaving(true)
    setFormError(null)
    try {
      await employeeMasterApi.remove(item.id)
      setMessage(`${item.fullName} deleted.`)
      closeForm()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  // Only a Super Admin edits employee details; location admins open the record to manage its login.
  const readOnly = !!editing && !isSuperAdmin
  const showCamera = !readOnly && (!editing || !editing.hasFace || retakeFace)
  const editingLogin = editing?.userId ? usersById.get(editing.userId) : undefined

  return (
    <div>
      {saving ? <LoadingMask label="Saving…" /> : null}
      {loading ? <Spinner label="Loading employees…" /> : null}
      <PageHeader
        title="Employees"
        subtitle={
          isSuperAdmin
            ? 'Add every employee here with their face and details, and manage their logins.'
            : 'Add employees at your location and manage their logins. Only a Super Admin can edit or deactivate employees.'
        }
        actions={
          <div className="flex flex-wrap gap-2">
            <TextInput
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search name, ID, mobile, email"
              className="w-full min-w-[14rem] sm:w-60"
            />
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
            {isSuperAdmin ? <ExcelImportButtons kind="employees" label="Employees" onImported={load} /> : null}
            <Button variant="amber" onClick={startCreate}>Add employee</Button>
            <DeleteSelectedMasterButton type="employees" label="Employees" ids={selection.ids} onDeleted={load} />
            <ClearAllMasterButton type="employees" label="Employees" onCleared={load} />
          </div>
        }
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <Panel className="overflow-x-auto p-0">
        {!loading && sorted.length === 0 ? <div className="p-4"><EmptyState title="No employees" /></div> : null}
        {!loading && sorted.length > 0 ? (
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr className="whitespace-nowrap">
                {isSuperAdmin ? <SelectPageHeader selection={selection} pageIds={pageItems.map((e) => e.id)} /> : null}
                <th className="px-4 py-3">Face</th>
                <SortHeader sortKey="name" sort={sort} onSort={onSort}>Employee</SortHeader>
                <SortHeader sortKey="dob" sort={sort} onSort={onSort}>Date of birth</SortHeader>
                <th className="px-4 py-3">Contact</th>
                <SortHeader sortKey="department" sort={sort} onSort={onSort}>Department</SortHeader>
                <SortHeader sortKey="login" sort={sort} onSort={onSort}>Login</SortHeader>
                <SortHeader sortKey="status" sort={sort} onSort={onSort}>Status</SortHeader>
                <th className="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {pageItems.map((item) => {
                const login = item.userId ? usersById.get(item.userId) : undefined
                return (
                  <tr key={item.id} className="border-t border-gray-100 align-middle">
                    {isSuperAdmin ? <SelectRowCell selection={selection} id={item.id} label={item.fullName} /> : null}
                    <td className="px-4 py-2">
                      {item.photoUrl ? (
                        <SecureImage src={item.photoUrl} alt={item.fullName} className="h-10 w-10 rounded-full object-cover" />
                      ) : (
                        <span
                          className="inline-flex h-10 w-10 items-center justify-center rounded-full bg-amber-50 text-[10px] font-semibold text-amber-700 ring-1 ring-amber-200"
                          title="No face enrolled. Edit the employee to capture it."
                        >
                          No face
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <div className="whitespace-nowrap font-medium">{titledName(item)}</div>
                      <div className="text-xs text-ink-muted">
                        {[item.code, item.designation].filter(Boolean).join(' · ') || '—'}
                      </div>
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      <div>{formatDate(item.dateOfBirth)}</div>
                      <div className="text-xs text-ink-muted">{genderLabel(item.gender)}</div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="whitespace-nowrap">{item.phone || '—'}</div>
                      <div className="max-w-[16rem] truncate text-xs text-ink-muted" title={item.email || undefined}>{item.email || '—'}</div>
                    </td>
                    <td className="px-4 py-3">
                      <div>{item.departmentName}</div>
                      <div className="text-xs text-ink-muted">{item.locationName || 'All locations'}</div>
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      {item.userName ? (
                        <>
                          <div className="font-medium">{item.userName}</div>
                          <div className="text-xs text-ink-muted">
                            {login ? roleLabel(login.roles[0] ?? '') : ''}
                            {login && !login.isActive ? <span className="ml-1 font-medium text-amber-700">· Blocked</span> : null}
                          </div>
                        </>
                      ) : (
                        <span className="text-ink-muted">No login</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={
                          item.isActive
                            ? 'rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700 ring-1 ring-emerald-200'
                            : 'rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600 ring-1 ring-gray-200'
                        }
                      >
                        {item.isActive ? 'Active' : 'Inactive'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex justify-end whitespace-nowrap">
                        <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        ) : null}
        {!loading ? <Pagination {...pagination} /> : null}
      </Panel>

      {!loading && sharedLogins.length > 0 ? (
        <Panel className="mt-6 overflow-x-auto p-0">
          <div className="border-b border-gray-100 px-4 py-3">
            <h2 className="text-sm font-semibold text-steel">Logins without an employee record</h2>
            <p className="text-xs text-ink-muted">
              Shared desk and system accounts. To give a person their own login, add them as an employee and use Create login.
            </p>
          </div>
          <table className="min-w-full text-left text-sm">
            <thead className="bg-gray-50 text-gray-500">
              <tr className="whitespace-nowrap">
                <th className="px-4 py-3">Login</th>
                <th className="px-4 py-3">Role</th>
                <th className="px-4 py-3">Location</th>
                <th className="px-4 py-3">Sign-in</th>
                <th className="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {sharedLogins.map((u) => (
                <tr key={u.id} className="border-t border-gray-100">
                  <td className="px-4 py-3">
                    <div className="font-medium">{u.fullName}</div>
                    <div className="text-xs text-ink-muted">{u.username} · {u.email}</div>
                  </td>
                  <td className="px-4 py-3 whitespace-nowrap">{u.roles.map(roleLabel).join(', ')}</td>
                  <td className="px-4 py-3">
                    {u.locationName ?? (u.roles.includes('SuperAdmin')
                      ? <span className="text-ink-muted">All locations</span>
                      : <span className="font-medium text-amber-700">Not assigned</span>)}
                  </td>
                  <td className="px-4 py-3">{u.isActive ? 'Allowed' : <span className="font-medium text-amber-700">Blocked</span>}</td>
                  <td className="px-4 py-3 text-right">
                    <Button size="sm" variant="secondary" onClick={() => setManaging({ user: u, employee: null })}>Login</Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      ) : null}

      <Modal
        open={formOpen}
        title={editing ? (readOnly ? 'Employee' : 'Edit employee') : 'Add employee'}
        subtitle={editing ? titledName(editing) : 'All fields marked * and the face are required.'}
        onClose={closeForm}
        size="xl"
      >
        <form onSubmit={save} className="grid gap-4 lg:grid-cols-[1fr_260px]">
          {formError ? <div className="lg:col-span-2"><Alert tone="error">{formError}</Alert></div> : null}
          <fieldset disabled={readOnly} className="grid min-w-0 content-start gap-3 sm:grid-cols-2">
            <div>
              <FieldLabel>Employee ID *</FieldLabel>
              <TextInput required maxLength={50} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} />
            </div>
            <div>
              <FieldLabel htmlFor="employee-name">Name *</FieldLabel>
              <div className="flex gap-2">
                <TextSelect
                  aria-label="Title"
                  required
                  className="w-24 shrink-0"
                  value={form.title ?? ''}
                  onChange={(e) => {
                    const title = TITLES.find((t) => t.value === e.target.value)
                    setForm({ ...form, title: title?.value ?? null, gender: form.gender ?? title?.gender ?? null })
                  }}
                >
                  <option value="">Title</option>
                  {TITLES.map((t) => (
                    <option key={t.value} value={t.value}>{t.value}.</option>
                  ))}
                </TextSelect>
                <TextInput
                  id="employee-name"
                  required
                  maxLength={150}
                  value={form.fullName}
                  onChange={(e) => setForm({ ...form, fullName: e.target.value })}
                />
              </div>
            </div>
            <div>
              <FieldLabel>Date of birth *</FieldLabel>
              <TextInput
                required
                type="date"
                max={today()}
                value={form.dateOfBirth}
                onChange={(e) => setForm({ ...form, dateOfBirth: e.target.value })}
              />
            </div>
            <div>
              <FieldLabel>Sex *</FieldLabel>
              <TextSelect
                required
                value={form.gender ?? ''}
                onChange={(e) => setForm({ ...form, gender: e.target.value ? (Number(e.target.value) as EmployeeGender) : null })}
              >
                <option value="">Select</option>
                {GENDERS.map((g) => (
                  <option key={g.value} value={g.value}>{g.label}</option>
                ))}
              </TextSelect>
            </div>
            <div>
              <FieldLabel>Mobile number *</FieldLabel>
              <TextInput
                required
                inputMode="numeric"
                autoComplete="tel-national"
                placeholder="10-digit number"
                maxLength={10}
                value={form.phone}
                onChange={(e) => setForm({ ...form, phone: sanitizePhoneDigits(e.target.value) })}
              />
            </div>
            <div>
              <FieldLabel>Email *</FieldLabel>
              <TextInput required type="email" maxLength={100} value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Designation</FieldLabel>
              <TextSelect value={form.designation || ''} onChange={(e) => setForm({ ...form, designation: e.target.value })}>
                <option value="">Select designation</option>
                {designations.map((d) => (
                  <option key={d.id} value={d.name}>{d.name}</option>
                ))}
                {form.designation && !designations.some((d) => d.name === form.designation) ? (
                  <option value={form.designation}>{form.designation}</option>
                ) : null}
              </TextSelect>
            </div>
            <div>
              <FieldLabel>Intercom</FieldLabel>
              <TextInput maxLength={50} value={form.intercom || ''} onChange={(e) => setForm({ ...form, intercom: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Location</FieldLabel>
              <TextSelect
                value={form.locationId || ''}
                disabled={!!userLocationId}
                onChange={(e) => {
                  const nextLoc = e.target.value || null
                  const nextDepts = nextLoc
                    ? departments.filter((d) => !d.locationId || d.locationId === nextLoc)
                    : departments
                  const keepDept = nextDepts.some((d) => d.id === form.departmentId)
                  setForm({ ...form, locationId: nextLoc, departmentId: keepDept ? form.departmentId : (nextDepts[0]?.id || '') })
                }}
              >
                {userLocationId ? null : <option value="">All locations</option>}
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
          </fieldset>

          <div className="space-y-2">
            <FieldLabel>Face *</FieldLabel>
            {showCamera ? (
              <>
                {formOpen ? (
                  <FaceCapture
                    photoUrl={form.facePhotoBase64 || null}
                    onCapture={(photo) => setForm((f) => ({ ...f, facePhotoBase64: photo }))}
                    captureLabel="Capture face"
                  />
                ) : null}
                {form.facePhotoBase64 ? (
                  <Button type="button" variant="secondary" className="w-full" onClick={() => setForm({ ...form, facePhotoBase64: null })}>
                    Retake
                  </Button>
                ) : null}
                {editing?.hasFace ? (
                  <Button
                    type="button"
                    variant="ghost"
                    className="w-full"
                    onClick={() => {
                      setRetakeFace(false)
                      setForm({ ...form, facePhotoBase64: null })
                    }}
                  >
                    Keep the current face
                  </Button>
                ) : null}
              </>
            ) : (
              <>
                {editing?.photoUrl ? (
                  <SecureImage src={editing.photoUrl} alt={editing.fullName} className="aspect-[3/4] w-full rounded-xl object-cover" />
                ) : (
                  <p className="rounded-xl bg-amber-50 px-3 py-6 text-center text-xs font-medium text-amber-700">No face enrolled</p>
                )}
                {readOnly ? null : (
                  <Button type="button" variant="secondary" className="w-full" onClick={() => setRetakeFace(true)}>
                    Capture a new face
                  </Button>
                )}
              </>
            )}
            <p className="text-xs text-ink-muted">
              Reception recognises the employee by this face at any location, and it becomes their face sign-in when a login is created.
            </p>

            {editing ? (
              <div className="space-y-2 border-t border-gray-100 pt-3">
                <FieldLabel>Login</FieldLabel>
                {editingLogin ? (
                  <>
                    <p className="text-sm">
                      <span className="font-medium">{editingLogin.username}</span>
                      <span className="text-ink-muted"> · {roleLabel(editingLogin.roles[0] ?? '')}</span>
                      {!editingLogin.isActive ? <span className="ml-1 font-medium text-amber-700">· Blocked</span> : null}
                    </p>
                    <Button
                      type="button"
                      variant="secondary"
                      className="w-full"
                      onClick={() => {
                        const employee = editing
                        closeForm()
                        setManaging({ user: editingLogin, employee })
                      }}
                    >
                      Manage login
                    </Button>
                  </>
                ) : editing.userName ? (
                  <p className="text-sm font-medium">{editing.userName}</p>
                ) : editing.isActive ? (
                  <>
                    <p className="text-xs text-ink-muted">No login yet.</p>
                    <Button
                      type="button"
                      variant="secondary"
                      className="w-full"
                      onClick={() => {
                        const employee = editing
                        closeForm()
                        setLoginFor(employee)
                      }}
                    >
                      Create login
                    </Button>
                  </>
                ) : (
                  <p className="text-xs text-ink-muted">Reactivate the employee to create a login.</p>
                )}
              </div>
            ) : null}
          </div>

          <div className="flex flex-wrap items-center gap-2 border-t border-gray-100 pt-3 lg:col-span-2">
            {editing?.isActive && !readOnly ? (
              <Button type="button" variant="danger" disabled={saving} onClick={() => void deactivate(editing)}>
                Deactivate employee
              </Button>
            ) : null}
            {editing && !editing.isActive && !readOnly ? (
              <Button type="button" variant="secondary" disabled={saving} onClick={() => void persist(true, `${editing.fullName} reactivated.`)}>
                Reactivate employee
              </Button>
            ) : null}
            {editing && isSuperAdmin ? (
              <Button type="button" variant="danger" disabled={saving} onClick={() => void remove(editing)}>
                Delete employee
              </Button>
            ) : null}
            <div className="ml-auto flex gap-2">
              <Button type="button" variant="secondary" onClick={closeForm}>{readOnly ? 'Close' : 'Cancel'}</Button>
              {readOnly ? null : <Button type="submit" loading={saving}>Save</Button>}
            </div>
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

      {managing ? (
        <ManageLoginModal
          user={managing.user}
          employee={managing.employee}
          departments={departments}
          locations={locations}
          onClose={() => setManaging(null)}
          onChanged={async (text) => {
            setMessage(text)
            await load()
          }}
        />
      ) : null}
    </div>
  )
}
