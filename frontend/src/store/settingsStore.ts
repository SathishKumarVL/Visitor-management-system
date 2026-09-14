import { create } from 'zustand'
import { settingsApi } from '../lib/api'
import type { SettingsDto } from '../types/api'
import { assetUrl } from '../lib/utils'

const defaults: SettingsDto = {
  companyName: 'TIAANO',
  logoPath: '/branding/tiaano-logo.png',
  visitorIdPrefix: 'TIA',
  visitorPassValidityHours: 12,
  approvalRequired: true,
  walkInApprovalRequired: true,
  photoRequired: false,
  idVerificationRequired: false,
  maxVisitDurationWarningMinutes: 240,
  defaultEntryGate: 'Main Gate',
  defaultExitGate: 'Main Gate',
  sessionTimeoutMinutes: 480,
}

interface SettingsState {
  settings: SettingsDto
  logoSrc: string
  loaded: boolean
  load: () => Promise<void>
  update: (next: SettingsDto) => Promise<SettingsDto>
}

export const useSettingsStore = create<SettingsState>((set) => ({
  settings: defaults,
  logoSrc: assetUrl(defaults.logoPath),
  loaded: false,
  load: async () => {
    try {
      const settings = await settingsApi.get()
      set({ settings, logoSrc: assetUrl(settings.logoPath), loaded: true })
    } catch {
      set({ loaded: true })
    }
  },
  update: async (next) => {
    const settings = await settingsApi.update(next)
    set({ settings, logoSrc: assetUrl(settings.logoPath) })
    return settings
  },
}))
