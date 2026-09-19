import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { CreateEmployeeRequest, EmployeeDto, MasterItemDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'

export function HostsPage() {
  const [items, setItems] = useState<EmployeeDto[]>([])
  const [departments, setDepartments] = useState<MasterItemDto[]>([])
  const [departmentId, setDepartmentId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<EmployeeDto | null>(null)
  const [form, setForm] = useState<CreateEmployeeRequest>({
    fullName: '',
    email: '',
    phone: '',
    intercom: '',
    designation: '',
    departmentId: '',
  })
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setItems(await mastersApi.hosts(departmentId || undefined, false))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [departmentId])

  useEffect(() => {
    void mastersApi.departments(false).then(setDepartments)
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function startCreate() {
    setEditing(null)
    setForm({
      fullName: '',
      email: '',
      phone: '',
      intercom: '',
      designation: '',
      departmentId: departmentId || departments[0]?.id || '',
    })
  }

  function startEdit(item: EmployeeDto) {
    setEditing(item)
    setForm({
      fullName: item.fullName,
      email: item.email || '',
      phone: item.phone || '',
      intercom: item.intercom || '',
      designation: item.designation || '',
      departmentId: item.departmentId,
      userId: item.userId,
    })
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      if (editing) await mastersApi.updateHost(editing.id, form)
      else await mastersApi.createHost(form)
      setMessage('Saved.')
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
      <PageHeader
        title="People to meet"
        actions={
          <div className="flex gap-2">
            <TextSelect value={departmentId} onChange={(e) => setDepartmentId(e.target.value)} className="w-full min-w-[12rem] sm:w-56">
              <option value="">All departments</option>
              {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </TextSelect>
            <Button variant="amber" onClick={startCreate}>Add person to meet</Button>
          </div>
        }
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <div className="grid gap-4 xl:grid-cols-[1fr_360px]">
        <Panel className="overflow-x-auto p-0">
          {loading ? <Spinner /> : null}
          {!loading && items.length === 0 ? <div className="p-4"><EmptyState title="No people to meet" /></div> : null}
          {!loading && items.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Department</th>
                  <th className="px-4 py-3">Intercom</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id} className="border-t border-gray-100">
                    <td className="px-4 py-3">
                      <div className="font-medium">{item.fullName}</div>
                      <div className="text-xs text-gray-500">{item.designation || item.email}</div>
                    </td>
                    <td className="px-4 py-3">{item.departmentName}</td>
                    <td className="px-4 py-3">{item.intercom || '—'}</td>
                    <td className="px-4 py-3 text-right">
                      <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </Panel>

        <Panel>
          <h2 className="mb-3 font-semibold text-steel">{editing ? 'Edit person to meet' : 'Create person to meet'}</h2>
          <form onSubmit={save} className="space-y-3">
            <div>
              <FieldLabel>Full name *</FieldLabel>
              <TextInput required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Department *</FieldLabel>
              <TextSelect required value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })}>
                <option value="">Select</option>
                {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
              </TextSelect>
            </div>
            <div>
              <FieldLabel>Email</FieldLabel>
              <TextInput type="email" value={form.email || ''} onChange={(e) => setForm({ ...form, email: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Phone</FieldLabel>
              <TextInput value={form.phone || ''} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Intercom</FieldLabel>
              <TextInput value={form.intercom || ''} onChange={(e) => setForm({ ...form, intercom: e.target.value })} />
            </div>
            <div>
              <FieldLabel>Designation</FieldLabel>
              <TextInput value={form.designation || ''} onChange={(e) => setForm({ ...form, designation: e.target.value })} />
            </div>
            <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
          </form>
        </Panel>
      </div>
    </div>
  )
}
