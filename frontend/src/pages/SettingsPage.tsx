import { useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage } from '../lib/api'
import type { SettingsDto } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'
import { useSettingsStore } from '../store/settingsStore'
import { BrandLogo } from '../components/BrandLogo'
import { cn } from '../lib/utils'

const SECTIONS = [
  { id: 'general', label: 'General' },
  { id: 'branding', label: 'Branding' },
  { id: 'visitor', label: 'Visitor Rules' },
  { id: 'pass', label: 'Pass Settings' },
  { id: 'security', label: 'Security' },
] as const

export function SettingsPage() {
  const { settings, load, update, loaded } = useSettingsStore()
  const [form, setForm] = useState<SettingsDto>(settings)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [section, setSection] = useState<(typeof SECTIONS)[number]['id']>('general')

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    setForm(settings)
  }, [settings])

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    setMessage(null)
    try {
      await update({
        ...form,
        approvalRequired: false,
        walkInApprovalRequired: false,
      })
      setMessage('Settings saved.')
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to save settings.'))
    } finally {
      setSaving(false)
    }
  }

  if (!loaded) return <Spinner />

  return (
    <div>
      <PageHeader eyebrow="Administration" title="Settings" subtitle="Company branding and visitor policy" />
      {error ? (
        <div className="mb-4">
          <Alert tone="error">{error}</Alert>
        </div>
      ) : null}
      {message ? (
        <div className="mb-4">
          <Alert tone="success">{message}</Alert>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[220px_1fr_240px]">
        <Panel className="h-fit p-2">
          <nav className="flex flex-col gap-1" aria-label="Settings categories">
            {SECTIONS.map((s) => (
              <button
                key={s.id}
                type="button"
                onClick={() => setSection(s.id)}
                className={cn(
                  'min-h-11 rounded-xl px-3 text-left text-sm font-medium transition',
                  section === s.id ? 'bg-brand-gradient text-white shadow-md' : 'text-ink-muted hover:bg-mint',
                )}
              >
                {s.label}
              </button>
            ))}
          </nav>
        </Panel>

        <Panel>
          <form onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-2">
            {section === 'general' || section === 'branding' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                    {section === 'branding' ? 'Branding' : 'General'}
                  </h2>
                </div>
                <div>
                  <FieldLabel htmlFor="companyName">Company name</FieldLabel>
                  <TextInput
                    id="companyName"
                    value={form.companyName}
                    onChange={(e) => setForm({ ...form, companyName: e.target.value })}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="logoPath">Logo path</FieldLabel>
                  <TextInput
                    id="logoPath"
                    value={form.logoPath}
                    onChange={(e) => setForm({ ...form, logoPath: e.target.value })}
                    placeholder="/branding/tiaano-logo.png"
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="visitorIdPrefix">Visitor ID prefix</FieldLabel>
                  <TextInput
                    id="visitorIdPrefix"
                    value={form.visitorIdPrefix}
                    onChange={(e) => setForm({ ...form, visitorIdPrefix: e.target.value })}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="defaultEntryGate">Default entry gate</FieldLabel>
                  <TextInput
                    id="defaultEntryGate"
                    value={form.defaultEntryGate}
                    onChange={(e) => setForm({ ...form, defaultEntryGate: e.target.value })}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="defaultExitGate">Default exit gate</FieldLabel>
                  <TextInput
                    id="defaultExitGate"
                    value={form.defaultExitGate}
                    onChange={(e) => setForm({ ...form, defaultExitGate: e.target.value })}
                  />
                </div>
              </>
            ) : null}

            {section === 'visitor' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Visitor Rules</h2>
                </div>
                <div>
                  <FieldLabel htmlFor="maxVisitDurationWarningMinutes">Long-stay warning (minutes)</FieldLabel>
                  <TextInput
                    id="maxVisitDurationWarningMinutes"
                    type="number"
                    value={form.maxVisitDurationWarningMinutes}
                    onChange={(e) => setForm({ ...form, maxVisitDurationWarningMinutes: Number(e.target.value) })}
                  />
                </div>
                <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
                  <input
                    type="checkbox"
                    checked={form.photoRequired}
                    onChange={(e) => setForm({ ...form, photoRequired: e.target.checked })}
                    className="accent-primary"
                  />
                  Photo required
                </label>
                <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
                  <input
                    type="checkbox"
                    checked={form.idVerificationRequired}
                    onChange={(e) => setForm({ ...form, idVerificationRequired: e.target.checked })}
                    className="accent-primary"
                  />
                  ID verification required
                </label>
              </>
            ) : null}

            {section === 'pass' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Pass Settings</h2>
                </div>
                <div>
                  <FieldLabel htmlFor="visitorPassValidityHours">Pass validity (hours)</FieldLabel>
                  <TextInput
                    id="visitorPassValidityHours"
                    type="number"
                    value={form.visitorPassValidityHours}
                    onChange={(e) => setForm({ ...form, visitorPassValidityHours: Number(e.target.value) })}
                  />
                </div>
              </>
            ) : null}

            {section === 'security' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Security</h2>
                </div>
                <div>
                  <FieldLabel htmlFor="sessionTimeoutMinutes">Session timeout (minutes)</FieldLabel>
                  <TextInput
                    id="sessionTimeoutMinutes"
                    type="number"
                    value={form.sessionTimeoutMinutes}
                    onChange={(e) => setForm({ ...form, sessionTimeoutMinutes: Number(e.target.value) })}
                  />
                </div>
              </>
            ) : null}

            <div className="sm:col-span-2">
              <Button type="submit" variant="primary" loading={saving}>
                {saving ? 'Saving…' : 'Save settings'}
              </Button>
            </div>
          </form>
        </Panel>

        <Panel className="h-fit text-center">
          <div className="text-sm text-ink-muted">Logo preview</div>
          <BrandLogo className="mx-auto mt-3 h-16" />
          <p className="mt-3 break-all text-xs text-ink-muted">{form.logoPath}</p>
        </Panel>
      </div>
    </div>
  )
}
