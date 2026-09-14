import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi, usersApi } from '../lib/api'
import type { CreateUserRequest, MasterItemDto, UpdateUserRequest, UserDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, PasswordInput, TextSelect } from '../components/ui/Field'

const ROLES = ['SuperAdmin', 'Admin', 'Reception', 'Security', 'Host']

export function UsersPage() {
  const [users, setUsers] = useState<UserDto[]>([])
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<UserDto | null>(null)
  const [createForm, setCreateForm] = useState<CreateUserRequest>({
    fullName: '',
    username: '',
    email: '',
    role: 'Reception',
    departmentId: null,
    password: '',
  })
  const [updateForm, setUpdateForm] = useState<UpdateUserRequest>({
    fullName: '',
    email: '',
    role: 'Reception',
    departmentId: null,
    isActive: true,
  })
  const [saving, setSaving] = useState(false)

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
  }, [load])

  function startEdit(user: UserDto) {
    setEditing(user)
    setUpdateForm({
      fullName: user.fullName,
      email: user.email,
      role: user.roles[0] || 'Reception',
      departmentId: user.departmentId ?? null,
      isActive: user.isActive,
    })
  }

  async function createUser(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await usersApi.create({
        ...createForm,
        departmentId: createForm.departmentId || null,
      })
      setMessage('User created.')
      setCreateForm({
        fullName: '',
        username: '',
        email: '',
        role: 'Reception',
        departmentId: null,
        password: '',
      })
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function saveUser(e: FormEvent) {
    e.preventDefault()
    if (!editing) return
    setSaving(true)
    setError(null)
    try {
      await usersApi.update(editing.id, {
        ...updateForm,
        departmentId: updateForm.departmentId || null,
      })
      setMessage('User updated.')
      setEditing(null)
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <PageHeader title="Users" subtitle="Manage system accounts and roles" />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <div className="grid gap-4 xl:grid-cols-[1fr_360px]">
        <Panel className="overflow-x-auto p-0">
          {loading ? <Spinner /> : null}
          {!loading && users.length === 0 ? <div className="p-4"><EmptyState title="No users" /></div> : null}
          {!loading && users.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <th className="px-4 py-3">User</th>
                  <th className="px-4 py-3">Roles</th>
                  <th className="px-4 py-3">Active</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {users.map((u) => (
                  <tr key={u.id} className="border-t border-gray-100">
                    <td className="px-4 py-3">
                      <div className="font-medium">{u.fullName}</div>
                      <div className="text-xs text-gray-500">{u.username} · {u.email}</div>
                    </td>
                    <td className="px-4 py-3">{u.roles.join(', ')}</td>
                    <td className="px-4 py-3">{u.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-4 py-3 text-right">
                      <Button size="sm" variant="secondary" onClick={() => startEdit(u)}>Edit</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </Panel>

        <div className="space-y-4">
          <Panel>
            <h2 className="mb-3 font-semibold text-steel">{editing ? 'Edit user' : 'Create user'}</h2>
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
                  <TextSelect value={updateForm.role} onChange={(e) => setUpdateForm({ ...updateForm, role: e.target.value })}>
                    {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                  </TextSelect>
                </div>
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
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" checked={updateForm.isActive} onChange={(e) => setUpdateForm({ ...updateForm, isActive: e.target.checked })} />
                  Active
                </label>
                <div className="flex gap-2">
                  <Button type="submit" disabled={saving}>Save</Button>
                  <Button type="button" variant="secondary" onClick={() => setEditing(null)}>Cancel</Button>
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
                  <TextSelect value={createForm.role} onChange={(e) => setCreateForm({ ...createForm, role: e.target.value })}>
                    {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                  </TextSelect>
                </div>
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
                  <FieldLabel>Password</FieldLabel>
                  <PasswordInput required minLength={6} value={createForm.password} onChange={(e) => setCreateForm({ ...createForm, password: e.target.value })} />
                </div>
                <Button type="submit" disabled={saving}>{saving ? 'Creating…' : 'Create user'}</Button>
              </form>
            )}
          </Panel>
        </div>
      </div>
    </div>
  )
}
