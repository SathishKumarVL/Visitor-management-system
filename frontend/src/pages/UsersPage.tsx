import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi, usersApi } from '../lib/api'
import type { CreateUserRequest, MasterItemDto, UpdateUserRequest, UserDto, UserFaceLoginDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, PasswordInput, TextSelect } from '../components/ui/Field'
import { menusForRole, type MenuKey } from '../lib/roles'
import { Modal } from '../components/ui/Modal'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'
import { useSort } from '../lib/useSort'
import { SortHeader } from '../components/ui/SortHeader'
import { FaceCapture } from '../components/FaceCapture'
import { formatDateTime } from '../lib/utils'

const ROLES = ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host'] as const

function roleLabel(role: string): string {
  return role === 'Host' ? 'Person to meet' : role
}

function defaultMenusForRole(role: string): string[] {
  return menusForRole(role).map((m) => m.key)
}

export function UsersPage() {
  const [users, setUsers] = useState<UserDto[]>([])
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const { sorted, sort, onSort } = useSort(users, {
    user: (u) => u.fullName,
    roles: (u) => u.roles.map(roleLabel).join(', '),
    location: (u) => u.locationName,
    active: (u) => (u.isActive ? 0 : 1),
  })
  const { pageItems, pagination } = usePagination(sorted, 20, `${sort?.key}|${sort?.dir}`)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<UserDto | null>(null)
  const [createForm, setCreateForm] = useState<CreateUserRequest>(() => ({
    fullName: '',
    username: '',
    email: '',
    role: 'Reception',
    departmentId: null,
    locationId: null,
    allowedMenuKeys: defaultMenusForRole('Reception'),
    password: '',
  }))
  const [updateForm, setUpdateForm] = useState<UpdateUserRequest>({
    fullName: '',
    email: '',
    role: 'Reception',
    departmentId: null,
    locationId: null,
    allowedMenuKeys: defaultMenusForRole('Reception'),
    isActive: true,
  })
  const [saving, setSaving] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [resetting, setResetting] = useState<UserDto | null>(null)
  const [resetPassword, setResetPassword] = useState('')
  const [resetMustChange, setResetMustChange] = useState(true)
  const [resetError, setResetError] = useState<string | null>(null)
  const [faceUser, setFaceUser] = useState<UserDto | null>(null)
  const [faces, setFaces] = useState<UserFaceLoginDto[]>([])
  const [facesLoading, setFacesLoading] = useState(false)
  const [facePerson, setFacePerson] = useState('')
  const [facePhoto, setFacePhoto] = useState<string | null>(null)
  const [faceError, setFaceError] = useState<string | null>(null)
  const [faceMessage, setFaceMessage] = useState<string | null>(null)
  const [faceSaving, setFaceSaving] = useState(false)

  const createMenuOptions = useMemo(() => menusForRole(createForm.role), [createForm.role])
  const updateMenuOptions = useMemo(() => menusForRole(updateForm.role), [updateForm.role])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setUsers(await usersApi.list())
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
    void mastersApi.departments(false).then(setDepartments)
    void mastersApi.locations(true).then(setLocations)
  }, [load])

  function toggleMenu(
    keys: string[] | undefined,
    key: MenuKey,
    onChange: (next: string[]) => void,
  ) {
    const current = new Set(keys ?? [])
    if (current.has(key)) current.delete(key)
    else current.add(key)
    onChange([...current])
  }

  function closeForm() {
    setCreateOpen(false)
    setEditing(null)
    setFormError(null)
  }

  function startCreate() {
    setEditing(null)
    setFormError(null)
    setMessage(null)
    setCreateOpen(true)
  }

  function startEdit(user: UserDto) {
    setCreateOpen(false)
    setFormError(null)
    setMessage(null)
    setEditing(user)
    const role = user.roles[0] || 'Reception'
    const allowed =
      user.allowedMenuKeys && user.allowedMenuKeys.length > 0
        ? user.allowedMenuKeys
        : defaultMenusForRole(role)
    setUpdateForm({
      fullName: user.fullName,
      email: user.email,
      role,
      departmentId: user.departmentId ?? null,
      locationId: user.locationId ?? null,
      allowedMenuKeys: allowed.filter((k) => defaultMenusForRole(role).includes(k)),
      isActive: user.isActive,
    })
  }

  async function createUser(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setFormError(null)
    try {
      await usersApi.create({
        ...createForm,
        departmentId: createForm.departmentId || null,
        locationId: createForm.locationId || null,
        allowedMenuKeys: createForm.allowedMenuKeys ?? [],
      })
      setMessage('User created.')
      setCreateForm({
        fullName: '',
        username: '',
        email: '',
        role: 'Reception',
        departmentId: null,
        locationId: null,
        allowedMenuKeys: defaultMenusForRole('Reception'),
        password: '',
      })
      closeForm()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function saveUser(e: FormEvent) {
    e.preventDefault()
    if (!editing) return
    setSaving(true)
    setFormError(null)
    try {
      await usersApi.update(editing.id, {
        ...updateForm,
        departmentId: updateForm.departmentId || null,
        locationId: updateForm.locationId || null,
        allowedMenuKeys: updateForm.allowedMenuKeys ?? [],
      })
      setMessage('User updated.')
      closeForm()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  function startReset(user: UserDto) {
    setMessage(null)
    setResetError(null)
    setResetPassword('')
    setResetMustChange(true)
    setResetting(user)
  }

  async function submitReset(e: FormEvent) {
    e.preventDefault()
    if (!resetting) return
    setSaving(true)
    setResetError(null)
    try {
      await usersApi.resetPassword(resetting.id, resetPassword, resetMustChange)
      setMessage(`Password reset for ${resetting.fullName}.`)
      setResetting(null)
    } catch (err) {
      setResetError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  function setFaceCount(userId: string, count: number) {
    setUsers((prev) => prev.map((u) => (u.id === userId ? { ...u, faceLoginCount: count } : u)))
  }

  async function openFaces(user: UserDto) {
    setMessage(null)
    setFaceUser(user)
    setFaces([])
    setFacePerson('')
    setFacePhoto(null)
    setFaceError(null)
    setFaceMessage(null)
    setFacesLoading(true)
    try {
      setFaces(await usersApi.listFaces(user.id))
    } catch (err) {
      setFaceError(apiErrorMessage(err))
    } finally {
      setFacesLoading(false)
    }
  }

  async function addFace(e: FormEvent) {
    e.preventDefault()
    if (!faceUser) return
    if (!facePhoto) {
      setFaceError('Capture the person’s face first.')
      return
    }
    setFaceSaving(true)
    setFaceError(null)
    setFaceMessage(null)
    try {
      const added = await usersApi.addFace(faceUser.id, facePerson.trim(), facePhoto)
      const next = [...faces, added].sort((a, b) => a.personName.localeCompare(b.personName))
      setFaces(next)
      setFaceCount(faceUser.id, next.length)
      setFaceMessage(`Face registered for ${added.personName}. They can now sign in with their face.`)
      setFacePerson('')
      setFacePhoto(null)
    } catch (err) {
      setFaceError(apiErrorMessage(err))
    } finally {
      setFaceSaving(false)
    }
  }

  async function removeFace(face: UserFaceLoginDto) {
    if (!faceUser) return
    if (!window.confirm(`Remove ${face.personName}'s face? They will no longer be able to sign in with it.`)) return
    setFaceSaving(true)
    setFaceError(null)
    setFaceMessage(null)
    try {
      await usersApi.removeFace(faceUser.id, face.id)
      const next = faces.filter((f) => f.id !== face.id)
      setFaces(next)
      setFaceCount(faceUser.id, next.length)
      setFaceMessage(`Removed ${face.personName}'s face.`)
    } catch (err) {
      setFaceError(apiErrorMessage(err))
    } finally {
      setFaceSaving(false)
    }
  }

  function MenuPicker({
    options,
    selected,
    onChange,
  }: {
    options: { key: MenuKey; label: string }[]
    selected: string[]
    onChange: (next: string[]) => void
  }) {
    return (
      <div>
        <FieldLabel>Visible menus</FieldLabel>
        <p className="mb-2 text-xs text-gray-500">
          Choose which sidebar items this user can see. Options are limited by their role.
        </p>
        <div className="max-h-48 space-y-1 overflow-y-auto rounded-md border border-gray-200 p-2">
          {options.map((item) => (
            <label key={item.key} className="flex min-h-9 cursor-pointer items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={selected.includes(item.key)}
                onChange={() => toggleMenu(selected, item.key, onChange)}
              />
              {item.label}
            </label>
          ))}
        </div>
        <div className="mt-2 flex gap-2">
          <Button
            type="button"
            size="sm"
            variant="secondary"
            onClick={() => onChange(options.map((o) => o.key))}
          >
            Select all
          </Button>
          <Button type="button" size="sm" variant="secondary" onClick={() => onChange([])}>
            Clear
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div>
      {saving ? <LoadingMask label="Saving user…" /> : null}
      {loading ? <Spinner label="Loading users…" /> : null}
      <PageHeader
        title="Users"
        subtitle="Manage system accounts, roles, and menu access"
        actions={<Button variant="amber" onClick={startCreate}>Add user</Button>}
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <Panel className="overflow-x-auto p-0">
          {!loading && users.length === 0 ? <div className="p-4"><EmptyState title="No users" /></div> : null}
          {!loading && users.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <SortHeader sortKey="user" sort={sort} onSort={onSort}>User</SortHeader>
                  <SortHeader sortKey="roles" sort={sort} onSort={onSort}>Roles</SortHeader>
                  <th className="px-4 py-3">Menus</th>
                  <SortHeader sortKey="location" sort={sort} onSort={onSort}>Location</SortHeader>
                  <SortHeader sortKey="active" sort={sort} onSort={onSort}>Active</SortHeader>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {pageItems.map((u) => (
                  <tr key={u.id} className="border-t border-gray-100">
                    <td className="px-4 py-3">
                      <div className="font-medium">{u.fullName}</div>
                      <div className="text-xs text-gray-500">{u.username} · {u.email}</div>
                      {u.faceLoginCount ? (
                        <div className="mt-1 inline-flex rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                          Face sign-in · {u.faceLoginCount} {u.faceLoginCount === 1 ? 'face' : 'faces'}
                        </div>
                      ) : null}
                    </td>
                    <td className="px-4 py-3">{u.roles.map(roleLabel).join(', ')}</td>
                    <td className="px-4 py-3 text-xs text-gray-600">
                      {(u.allowedMenuKeys?.length
                        ? u.allowedMenuKeys
                        : defaultMenusForRole(u.roles[0] || 'Reception')
                      ).length}{' '}
                      items
                    </td>
                    <td className="px-4 py-3">
                      {u.locationName ?? <span className="text-gray-500">All locations</span>}
                    </td>
                    <td className="px-4 py-3">{u.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        <Button size="sm" variant="secondary" onClick={() => startEdit(u)}>Edit</Button>
                        <Button size="sm" variant="secondary" onClick={() => startReset(u)}>Reset password</Button>
                        <Button size="sm" variant="secondary" onClick={() => void openFaces(u)}>Face sign-in</Button>
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
        open={createOpen || editing !== null}
        title={editing ? 'Edit user' : 'Add user'}
        subtitle={editing ? `${editing.fullName} · ${editing.username}` : undefined}
        onClose={closeForm}
      >
            {formError ? <div className="mb-3"><Alert tone="error">{formError}</Alert></div> : null}
            {editing ? (
              <form onSubmit={saveUser} className="space-y-3">
                <div>
                  <FieldLabel>Full name</FieldLabel>
                  <TextInput required value={updateForm.fullName} onChange={(e) => setUpdateForm({ ...updateForm, fullName: e.target.value })} />
                </div>
                <div>
                  <FieldLabel>Email</FieldLabel>
                  <TextInput required type="email" value={updateForm.email} onChange={(e) => setUpdateForm({ ...updateForm, email: e.target.value })} />
                </div>
                <div>
                  <FieldLabel>Role</FieldLabel>
                  <TextSelect
                    value={updateForm.role}
                    onChange={(e) => {
                      const role = e.target.value
                      setUpdateForm({
                        ...updateForm,
                        role,
                        allowedMenuKeys: defaultMenusForRole(role),
                      })
                    }}
                  >
                    {ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}
                  </TextSelect>
                </div>
                <MenuPicker
                  options={updateMenuOptions}
                  selected={updateForm.allowedMenuKeys ?? []}
                  onChange={(allowedMenuKeys) => setUpdateForm({ ...updateForm, allowedMenuKeys })}
                />
                <div>
                  <FieldLabel>Department</FieldLabel>
                  <TextSelect
                    value={updateForm.departmentId || ''}
                    onChange={(e) => setUpdateForm({ ...updateForm, departmentId: e.target.value || null })}
                  >
                    <option value="">None</option>
                    {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
                  </TextSelect>
                </div>
                <div>
                  <FieldLabel htmlFor="edit-location">Location</FieldLabel>
                  <TextSelect
                    id="edit-location"
                    value={updateForm.locationId || ''}
                    onChange={(e) => setUpdateForm({ ...updateForm, locationId: e.target.value || null })}
                  >
                    <option value="">All locations (no restriction)</option>
                    {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                  </TextSelect>
                  <p className="mt-1 text-xs text-gray-500">
                    Choosing a location limits this user to that location&apos;s visitors, reports and roster.
                  </p>
                </div>
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" checked={updateForm.isActive} onChange={(e) => setUpdateForm({ ...updateForm, isActive: e.target.checked })} />
                  Active
                </label>
                <div className="flex justify-end gap-2 border-t border-gray-100 pt-3">
                  <Button type="button" variant="secondary" onClick={closeForm}>Cancel</Button>
                  <Button type="submit" disabled={saving}>Save</Button>
                </div>
              </form>
            ) : (
              <form onSubmit={createUser} className="space-y-3">
                <div>
                  <FieldLabel>Full name</FieldLabel>
                  <TextInput required value={createForm.fullName} onChange={(e) => setCreateForm({ ...createForm, fullName: e.target.value })} />
                </div>
                <div>
                  <FieldLabel>Username</FieldLabel>
                  <TextInput required value={createForm.username} onChange={(e) => setCreateForm({ ...createForm, username: e.target.value })} />
                </div>
                <div>
                  <FieldLabel>Email</FieldLabel>
                  <TextInput required type="email" value={createForm.email} onChange={(e) => setCreateForm({ ...createForm, email: e.target.value })} />
                </div>
                <div>
                  <FieldLabel>Role</FieldLabel>
                  <TextSelect
                    value={createForm.role}
                    onChange={(e) => {
                      const role = e.target.value
                      setCreateForm({
                        ...createForm,
                        role,
                        allowedMenuKeys: defaultMenusForRole(role),
                      })
                    }}
                  >
                    {ROLES.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}
                  </TextSelect>
                </div>
                <MenuPicker
                  options={createMenuOptions}
                  selected={createForm.allowedMenuKeys ?? []}
                  onChange={(allowedMenuKeys) => setCreateForm({ ...createForm, allowedMenuKeys })}
                />
                <div>
                  <FieldLabel>Department</FieldLabel>
                  <TextSelect
                    value={createForm.departmentId || ''}
                    onChange={(e) => setCreateForm({ ...createForm, departmentId: e.target.value || null })}
                  >
                    <option value="">None</option>
                    {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
                  </TextSelect>
                </div>
                <div>
                  <FieldLabel htmlFor="create-location">Location</FieldLabel>
                  <TextSelect
                    id="create-location"
                    value={createForm.locationId || ''}
                    onChange={(e) => setCreateForm({ ...createForm, locationId: e.target.value || null })}
                  >
                    <option value="">All locations (no restriction)</option>
                    {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                  </TextSelect>
                  <p className="mt-1 text-xs text-gray-500">
                    Choosing a location limits this user to that location&apos;s visitors, reports and roster.
                  </p>
                </div>
                <div>
                  <FieldLabel>Password</FieldLabel>
                  <PasswordInput required minLength={10} autoComplete="new-password" value={createForm.password} onChange={(e) => setCreateForm({ ...createForm, password: e.target.value })} />
                </div>
                <div className="flex justify-end gap-2 border-t border-gray-100 pt-3">
                  <Button type="button" variant="secondary" onClick={closeForm}>Cancel</Button>
                  <Button type="submit" disabled={saving}>{saving ? 'Creating…' : 'Create user'}</Button>
                </div>
              </form>
            )}
      </Modal>

      <Modal
        open={resetting !== null}
        title="Reset password"
        subtitle={resetting ? `${resetting.fullName} · ${resetting.username}` : undefined}
        onClose={() => setResetting(null)}
      >
        <form onSubmit={submitReset} className="space-y-3">
          {resetError ? <Alert tone="error">{resetError}</Alert> : null}
          <div>
            <FieldLabel htmlFor="reset-password">New password</FieldLabel>
            <PasswordInput
              id="reset-password"
              required
              minLength={10}
              autoComplete="new-password"
              value={resetPassword}
              onChange={(e) => setResetPassword(e.target.value)}
            />
            <p className="mt-1 text-xs text-gray-500">At least 10 characters with upper, lower, digit and a symbol.</p>
          </div>
          <label className="flex min-h-11 items-center gap-2 text-sm">
            <input type="checkbox" checked={resetMustChange} onChange={(e) => setResetMustChange(e.target.checked)} />
            Ask the user to change it at next sign-in
          </label>
          <div className="flex justify-end gap-2 border-t border-gray-100 pt-3">
            <Button type="button" variant="secondary" onClick={() => setResetting(null)}>Cancel</Button>
            <Button type="submit" disabled={saving}>Reset password</Button>
          </div>
        </form>
      </Modal>

      <Modal
        open={faceUser !== null}
        title="Face sign-in"
        subtitle={faceUser ? `${faceUser.fullName} · ${faceUser.username}` : undefined}
        onClose={() => setFaceUser(null)}
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Optional. Everyone whose face is registered here can sign in to this login by looking at the camera,
            without typing the username or password. Register each person (for example each security guard on
            this shared login) under their own name.
          </p>
          {faceError ? <Alert tone="error">{faceError}</Alert> : null}
          {faceMessage ? <Alert tone="success">{faceMessage}</Alert> : null}

          <div>
            <h3 className="mb-2 text-sm font-semibold text-gray-800">Registered faces</h3>
            {facesLoading ? <Spinner label="Loading faces…" /> : null}
            {!facesLoading && faces.length === 0 ? (
              <p className="rounded-md border border-dashed border-gray-200 p-3 text-sm text-gray-500">
                No faces registered. This login signs in with the username and password only.
              </p>
            ) : null}
            {faces.length > 0 ? (
              <ul className="divide-y divide-gray-100 rounded-md border border-gray-200">
                {faces.map((f) => (
                  <li key={f.id} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                    <div>
                      <div className="font-medium">{f.personName}</div>
                      <div className="text-xs text-gray-500">
                        Added {formatDateTime(f.createdAt)}
                        {f.createdBy ? ` by ${f.createdBy}` : ''} · Last used{' '}
                        {f.lastUsedAt ? formatDateTime(f.lastUsedAt) : 'never'}
                      </div>
                    </div>
                    <Button size="sm" variant="secondary" disabled={faceSaving} onClick={() => void removeFace(f)}>
                      Remove
                    </Button>
                  </li>
                ))}
              </ul>
            ) : null}
          </div>

          <form onSubmit={addFace} className="space-y-3 border-t border-gray-100 pt-3">
            <h3 className="text-sm font-semibold text-gray-800">Add a face</h3>
            <div>
              <FieldLabel htmlFor="face-person">Person&apos;s name</FieldLabel>
              <TextInput
                id="face-person"
                required
                maxLength={100}
                placeholder="e.g. Ravi (night shift guard)"
                value={facePerson}
                onChange={(e) => setFacePerson(e.target.value)}
              />
            </div>
            <FaceCapture photoUrl={facePhoto} onCapture={setFacePhoto} captureLabel="Capture face" />
            {facePhoto ? (
              <Button type="button" variant="secondary" className="w-full" onClick={() => setFacePhoto(null)}>
                Retake
              </Button>
            ) : null}
            <div className="flex justify-end gap-2 border-t border-gray-100 pt-3">
              <Button type="button" variant="secondary" onClick={() => setFaceUser(null)}>Close</Button>
              <Button type="submit" disabled={faceSaving || !facePhoto || !facePerson.trim()} loading={faceSaving}>
                Register face
              </Button>
            </div>
          </form>
        </div>
      </Modal>
    </div>
  )
}
