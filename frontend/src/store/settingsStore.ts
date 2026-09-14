import { create } from 'zustand'
import { getStoredToken, settingsApi } from '../lib/api'
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

async function loadBrandingFallback(
  set: (partial: Partial<SettingsState>) => void,
  current: SettingsDto,
) {
  try {
    const branding = await settingsApi.getBranding()
    set({
      settings: {
        ...current,
        companyName: branding.companyName,
        logoPath: branding.logoPath,
      },
      logoSrc: assetUrl(branding.logoPath),
      loaded: true,
    })
  } catch {
    set({ loaded: true })
  }
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
  settings: defaults,
  logoSrc: assetUrl(defaults.logoPath),
  loaded: false,
  load: async () => {
    const token = getStoredToken()
    if (token) {
      try {
        const settings = await settingsApi.get()
        set({ settings, logoSrc: assetUrl(settings.logoPath), loaded: true })
        return
      } catch {
        await loadBrandingFallback(set, get().settings)
        return
      }
    }

    await loadBrandingFallback(set, get().settings)
  },
  update: async (next) => {
    const settings = await settingsApi.update(next)
    set({ settings, logoSrc: assetUrl(settings.logoPath) })
    return settings
  },
}))
