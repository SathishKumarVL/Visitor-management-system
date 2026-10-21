import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { apiErrorMessage, usersApi } from '../lib/api'
import type { EmployeeDto, MasterItemDto, UserDto, UserFaceLoginDto } from '../types/api'
import { Alert, Spinner } from './ui/Panel'
import { Button } from './ui/Button'
import { FieldLabel, PasswordInput, TextInput, TextSelect } from './ui/Field'
import { Modal } from './ui/Modal'
import { MENU_ITEMS, menusForRole, roleLabel, type MenuKey } from '../lib/roles'
import { formatDateTime, hasAnyRole, titledName } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

const ROLES = ['Host', 'Reception', 'Security', 'Admin', 'SuperAdmin'] as const
/** Only Super Admins span every location; these roles must be given one. */
const LOCATION_BOUND_ROLES: readonly string[] = ['Admin', 'Reception', 'Security']

function defaultMenus(role: string): string[] {
  return menusForRole(role).map((m) => m.key)
}

/**
 * Role, menus, sign-in, password and face sign-in of one login. A login that belongs to an employee takes its
 * name, email, department and location from the employee record; shared desk accounts keep their own.
 */
export function ManageLoginModal({
  user,
  employee,
  departments,
  locations,
  onClose,
  onChanged,
}: {
  user: UserDto
  employee: EmployeeDto | null
  departments: MasterItemDto[]
  locations: MasterItemDto[]
  onClose: () => void
  onChanged: (message: string) => void | Promise<void>
}) {
  const actorIsSuperAdmin = hasAnyRole(useAuthStore((s) => s.user?.roles) ?? [], ['SuperAdmin'])
  const initialRole = user.roles[0] || 'Reception'
  const [role, setRole] = useState(initialRole)
  const [isActive, setIsActive] = useState(user.isActive)
  const [menus, setMenus] = useState<string[]>(() => {
    const allowed = user.allowedMenuKeys?.length ? user.allowedMenuKeys : defaultMenus(initialRole)
    return allowed.filter((k) => defaultMenus(initialRole).includes(k))
  })
  const [fullName, setFullName] = useState(user.fullName)
  const [email, setEmail] = useState(user.email)
  const [departmentId, setDepartmentId] = useState(user.departmentId ?? '')
  const [locationId, setLocationId] = useState(user.locationId ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [resetOpen, setResetOpen] = useState(false)
  const [newPassword, setNewPassword] = useState('')
  const [mustChange, setMustChange] = useState(true)
  const [resetError, setResetError] = useState<string | null>(null)
  const [resetDone, setResetDone] = useState(false)

  const [faces, setFaces] = useState<UserFaceLoginDto[]>([])
  const [facesLoading, setFacesLoading] = useState(true)
  const [faceError, setFaceError] = useState<string | null>(null)

  const roles = ROLES.filter((r) => (r !== 'SuperAdmin' || actorIsSuperAdmin) && (r !== 'Host' || !!employee || initialRole === 'Host'))
  // A Super Admin may grant any menu; an Admin only the menus of the chosen role.
  const allMenus = useMemo(
    () => (actorIsSuperAdmin ? MENU_ITEMS.filter((item) => !item.alwaysVisible) : menusForRole(role)),
    [actorIsSuperAdmin, role],
  )
  const needsLocation = LOCATION_BOUND_ROLES.includes(role)
  const employeeMissingLocation = !!employee && needsLocation && !employee.locationId

  useEffect(() => {
    let cancelled = false
    usersApi
      .listFaces(user.id)
      .then((list) => !cancelled && setFaces(list))
      .catch((err) => !cancelled && setFaceError(apiErrorMessage(err)))
      .finally(() => !cancelled && setFacesLoading(false))
    return () => {
      cancelled = true
    }
  }, [user.id])

  async function save(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await usersApi.update(user.id, {
        fullName: employee ? employee.fullName : fullName.trim(),
        email: employee?.email || email.trim(),
        role,
        departmentId: (employee ? employee.departmentId : departmentId) || null,
        locationId: role === 'SuperAdmin' ? null : (employee ? employee.locationId : locationId) || null,
        allowedMenuKeys: menus,
        isActive,
      })
      await onChanged(`Login '${user.username}' updated.`)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function resetPassword(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setResetError(null)
    try {
      await usersApi.resetPassword(user.id, newPassword, mustChange)
      setResetDone(true)
      setResetOpen(false)
      setNewPassword('')
    } catch (err) {
      setResetError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function removeFace(face: UserFaceLoginDto) {
    if (!window.confirm(`Remove ${face.personName}'s face? They will no longer be able to sign in with it.`)) return
    setSaving(true)
    setFaceError(null)
    try {
      await usersApi.removeFace(user.id, face.id)
      setFaces((prev) => prev.filter((f) => f.id !== face.id))
    } catch (err) {
      setFaceError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  function toggleMenu(key: MenuKey) {
    setMenus((prev) => (prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]))
  }

  return (
    <Modal
      open
      title="Manage login"
      subtitle={`${employee ? titledName(employee) : user.fullName} · ${user.username}`}
      onClose={onClose}
      size="xl"
    >
      <div className="grid gap-5 lg:grid-cols-[1fr_280px]">
        <form onSubmit={save} className="grid content-start gap-3 sm:grid-cols-2">
          {error ? <div className="sm:col-span-2"><Alert tone="error">{error}</Alert></div> : null}
          <div>
            <FieldLabel htmlFor="login-role">Role</FieldLabel>
            <TextSelect
              id="login-role"
              value={role}
              onChange={(e) => {
                setRole(e.target.value)
                setMenus(defaultMenus(e.target.value))
              }}
            >
              {roles.map((r) => <option key={r} value={r}>{roleLabel(r)}</option>)}
            </TextSelect>
            {employeeMissingLocation ? (
              <p className="mt-1 text-xs font-medium text-amber-700">
                {employee!.fullName} has no location. Set it on the employee first; {roleLabel(role)} logins work at one location.
              </p>
            ) : null}
          </div>
          <div>
            <FieldLabel htmlFor="login-status">Sign-in</FieldLabel>
            <TextSelect id="login-status" value={isActive ? 'on' : 'off'} onChange={(e) => setIsActive(e.target.value === 'on')}>
              <option value="on">Allowed</option>
              <option value="off">Blocked</option>
            </TextSelect>
          </div>

          {employee ? (
            <p className="rounded-md bg-gray-50 px-3 py-2 text-xs text-ink-muted sm:col-span-2">
              Name, email, department and location come from the employee record
              {employee.locationName ? ` (${employee.locationName})` : ''}. Change them with Edit on the employee.
            </p>
          ) : (
            <>
              <div>
                <FieldLabel htmlFor="login-name">Name</FieldLabel>
                <TextInput id="login-name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
              </div>
              <div>
                <FieldLabel htmlFor="login-email">Email</FieldLabel>
                <TextInput id="login-email" required type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
              </div>
              <div>
                <FieldLabel htmlFor="login-department">Department</FieldLabel>
                <TextSelect id="login-department" value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
                  <option value="">None</option>
                  {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
                </TextSelect>
              </div>
              <div>
                <FieldLabel htmlFor="login-location">Location{needsLocation ? ' *' : ''}</FieldLabel>
                {role === 'SuperAdmin' ? (
                  <p className="flex min-h-11 items-center text-sm text-gray-700">All locations</p>
                ) : (
                  <TextSelect id="login-location" required={needsLocation} value={locationId} onChange={(e) => setLocationId(e.target.value)}>
                    <option value="">{needsLocation ? 'Select a location' : 'No location'}</option>
                    {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                  </TextSelect>
                )}
              </div>
            </>
          )}

          <div className="sm:col-span-2">
            <div className="mb-2 flex flex-wrap items-end justify-between gap-2">
              <div>
                <FieldLabel>Visible menus</FieldLabel>
                <p className="text-xs text-gray-500">Sidebar items this login can see.</p>
              </div>
              <div className="flex gap-2">
                <Button type="button" size="sm" variant="secondary" onClick={() => setMenus(allMenus.map((o) => o.key))}>
                  Select all
                </Button>
                <Button type="button" size="sm" variant="secondary" onClick={() => setMenus([])}>Clear</Button>
              </div>
            </div>
            <div className="grid gap-x-4 rounded-md border border-gray-200 px-3 py-1 sm:grid-cols-2 lg:grid-cols-3">
              {allMenus.map((item) => (
                <label key={item.key} className="flex min-h-9 cursor-pointer items-center gap-2 text-sm">
                  <input type="checkbox" checked={menus.includes(item.key)} onChange={() => toggleMenu(item.key)} />
                  <span>{item.label}</span>
                </label>
              ))}
            </div>
          </div>

          <div className="flex justify-end gap-2 border-t border-gray-100 pt-3 sm:col-span-2">
            <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
            <Button type="submit" loading={saving} disabled={employeeMissingLocation}>Save</Button>
          </div>
        </form>

        <div className="space-y-5 border-t border-gray-100 pt-4 lg:border-l lg:border-t-0 lg:pl-5 lg:pt-0">
          <section className="space-y-2">
            <h3 className="text-sm font-semibold text-gray-800">Password</h3>
            {resetDone ? <Alert tone="success">Password reset.</Alert> : null}
            {resetOpen ? (
              <form onSubmit={resetPassword} className="space-y-2">
                {resetError ? <Alert tone="error">{resetError}</Alert> : null}
                <PasswordInput
                  required
                  minLength={10}
                  autoComplete="new-password"
                  placeholder="New password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                />
                <p className="text-xs text-gray-500">At least 10 characters with an uppercase letter, a lowercase letter, a number and a special character (e.g. ! @ # $).</p>
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={mustChange} onChange={(e) => setMustChange(e.target.checked)} />
                  Change it at next sign-in
                </label>
                <div className="flex gap-2">
                  <Button type="button" size="sm" variant="secondary" onClick={() => setResetOpen(false)}>Cancel</Button>
                  <Button type="submit" size="sm" loading={saving}>Reset password</Button>
                </div>
              </form>
            ) : (
              <Button type="button" size="sm" variant="secondary" onClick={() => { setResetDone(false); setResetOpen(true) }}>
                Reset password
              </Button>
            )}
          </section>

          <section className="space-y-2">
            <h3 className="text-sm font-semibold text-gray-800">Face sign-in</h3>
            {faceError ? <Alert tone="error">{faceError}</Alert> : null}
            {facesLoading ? <Spinner label="Loading faces…" /> : null}
            {!facesLoading && faces.length === 0 ? (
              <p className="text-xs text-gray-500">
                No face registered; this login signs in with the username and password.
                {employee ? ' Capture the face with Edit on the employee.' : ''}
              </p>
            ) : null}
            {faces.length > 0 ? (
              <ul className="divide-y divide-gray-100 rounded-md border border-gray-200">
                {faces.map((f) => (
                  <li key={f.id} className="flex items-center justify-between gap-2 px-3 py-2 text-sm">
                    <div className="min-w-0">
                      <div className="truncate font-medium">{f.personName}</div>
                      <div className="text-xs text-gray-500">
                        Last used {f.lastUsedAt ? formatDateTime(f.lastUsedAt) : 'never'}
                      </div>
                    </div>
                    <Button size="sm" variant="secondary" disabled={saving} onClick={() => void removeFace(f)}>Remove</Button>
                  </li>
                ))}
              </ul>
            ) : null}
          </section>
        </div>
      </div>
    </Modal>
  )
}
