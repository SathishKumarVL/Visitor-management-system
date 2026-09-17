/** Theme and font presets applied via CSS variables on :root. */

export type ThemePresetId = 'tiaano' | 'ocean' | 'forest' | 'slate' | 'sunrise'
export type FontPresetId = 'inter' | 'sourceSans' | 'ibmPlex' | 'nunito'

export interface ThemePreset {
  id: ThemePresetId
  label: string
  description: string
  swatch: string
  vars: Record<string, string>
}

export interface FontPreset {
  id: FontPresetId
  label: string
  stack: string
}

export const THEME_PRESETS: ThemePreset[] = [
  {
    id: 'tiaano',
    label: 'TIAANO Teal',
    description: 'Default brand teal',
    swatch: 'linear-gradient(135deg, #005a62, #35c7c7)',
    vars: {
      '--primary': '#008f95',
      '--primary-dark': '#006f75',
      '--primary-deep': '#005a62',
      '--primary-light': '#35c7c7',
      '--accent': '#35c7c7',
      '--aqua-light': '#ddf7f6',
      '--mint': '#eef9f8',
      '--background': '#f5fbfa',
      '--border': '#d4e8e6',
      '--gradient-primary': 'linear-gradient(135deg, #005a62 0%, #008f95 48%, #35c7c7 100%)',
      '--gradient-soft': 'linear-gradient(160deg, #eef9f8 0%, #ddf7f6 45%, #f5fbfa 100%)',
      '--gradient-hero':
        'radial-gradient(circle at 18% 22%, rgba(53, 199, 199, 0.35), transparent 42%), radial-gradient(circle at 82% 12%, rgba(0, 143, 149, 0.28), transparent 38%), linear-gradient(145deg, #005a62 0%, #008f95 52%, #0b3440 100%)',
    },
  },
  {
    id: 'ocean',
    label: 'Ocean Blue',
    description: 'Cool deep blue',
    swatch: 'linear-gradient(135deg, #0b3d91, #4aa3ff)',
    vars: {
      '--primary': '#1a6fd0',
      '--primary-dark': '#0f4fa3',
      '--primary-deep': '#0b3d91',
      '--primary-light': '#4aa3ff',
      '--accent': '#4aa3ff',
      '--aqua-light': '#e3f0ff',
      '--mint': '#eef5fc',
      '--background': '#f4f8fc',
      '--border': '#c9dced',
      '--gradient-primary': 'linear-gradient(135deg, #0b3d91 0%, #1a6fd0 48%, #4aa3ff 100%)',
      '--gradient-soft': 'linear-gradient(160deg, #eef5fc 0%, #e3f0ff 45%, #f4f8fc 100%)',
      '--gradient-hero':
        'radial-gradient(circle at 18% 22%, rgba(74, 163, 255, 0.35), transparent 42%), radial-gradient(circle at 82% 12%, rgba(26, 111, 208, 0.28), transparent 38%), linear-gradient(145deg, #0b3d91 0%, #1a6fd0 52%, #0a2748 100%)',
    },
  },
  {
    id: 'forest',
    label: 'Forest',
    description: 'Fresh green',
    swatch: 'linear-gradient(135deg, #1b5e3b, #4caf75)',
    vars: {
      '--primary': '#2e7d4f',
      '--primary-dark': '#21633d',
      '--primary-deep': '#1b5e3b',
      '--primary-light': '#4caf75',
      '--accent': '#4caf75',
      '--aqua-light': '#e4f5ea',
      '--mint': '#eef8f1',
      '--background': '#f5faf6',
      '--border': '#c9e4d2',
      '--gradient-primary': 'linear-gradient(135deg, #1b5e3b 0%, #2e7d4f 48%, #4caf75 100%)',
      '--gradient-soft': 'linear-gradient(160deg, #eef8f1 0%, #e4f5ea 45%, #f5faf6 100%)',
      '--gradient-hero':
        'radial-gradient(circle at 18% 22%, rgba(76, 175, 117, 0.35), transparent 42%), radial-gradient(circle at 82% 12%, rgba(46, 125, 79, 0.28), transparent 38%), linear-gradient(145deg, #1b5e3b 0%, #2e7d4f 52%, #123024 100%)',
    },
  },
  {
    id: 'slate',
    label: 'Slate',
    description: 'Neutral steel',
    swatch: 'linear-gradient(135deg, #334155, #94a3b8)',
    vars: {
      '--primary': '#475569',
      '--primary-dark': '#334155',
      '--primary-deep': '#1e293b',
      '--primary-light': '#94a3b8',
      '--accent': '#64748b',
      '--aqua-light': '#eef2f6',
      '--mint': '#f1f5f9',
      '--background': '#f8fafc',
      '--border': '#d6dee8',
      '--gradient-primary': 'linear-gradient(135deg, #1e293b 0%, #475569 48%, #94a3b8 100%)',
      '--gradient-soft': 'linear-gradient(160deg, #f1f5f9 0%, #eef2f6 45%, #f8fafc 100%)',
      '--gradient-hero':
        'radial-gradient(circle at 18% 22%, rgba(148, 163, 184, 0.35), transparent 42%), radial-gradient(circle at 82% 12%, rgba(71, 85, 105, 0.28), transparent 38%), linear-gradient(145deg, #1e293b 0%, #475569 52%, #0f172a 100%)',
    },
  },
  {
    id: 'sunrise',
    label: 'Sunrise',
    description: 'Warm amber accent',
    swatch: 'linear-gradient(135deg, #9a3412, #f59e0b)',
    vars: {
      '--primary': '#c2410c',
      '--primary-dark': '#9a3412',
      '--primary-deep': '#7c2d12',
      '--primary-light': '#fb923c',
      '--accent': '#f59e0b',
      '--aqua-light': '#fff4e8',
      '--mint': '#fff8ef',
      '--background': '#fffbf5',
      '--border': '#f0dcc4',
      '--gradient-primary': 'linear-gradient(135deg, #7c2d12 0%, #c2410c 48%, #fb923c 100%)',
      '--gradient-soft': 'linear-gradient(160deg, #fff8ef 0%, #fff4e8 45%, #fffbf5 100%)',
      '--gradient-hero':
        'radial-gradient(circle at 18% 22%, rgba(245, 158, 11, 0.35), transparent 42%), radial-gradient(circle at 82% 12%, rgba(194, 65, 12, 0.28), transparent 38%), linear-gradient(145deg, #7c2d12 0%, #c2410c 52%, #431407 100%)',
    },
  },
]

export const FONT_PRESETS: FontPreset[] = [
  {
    id: 'inter',
    label: 'Inter',
    stack: '"Inter", "Segoe UI", system-ui, sans-serif',
  },
  {
    id: 'sourceSans',
    label: 'Source Sans 3',
    stack: '"Source Sans 3", "Segoe UI", system-ui, sans-serif',
  },
  {
    id: 'ibmPlex',
    label: 'IBM Plex Sans',
    stack: '"IBM Plex Sans", "Segoe UI", system-ui, sans-serif',
  },
  {
    id: 'nunito',
    label: 'Nunito Sans',
    stack: '"Nunito Sans", "Segoe UI", system-ui, sans-serif',
  },
]

export function applyBranding(themePreset?: string | null, fontPreset?: string | null) {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  const theme =
    THEME_PRESETS.find((t) => t.id === themePreset) ?? THEME_PRESETS[0]
  const font =
    FONT_PRESETS.find((f) => f.id === fontPreset) ?? FONT_PRESETS[0]

  for (const [key, value] of Object.entries(theme.vars)) {
    root.style.setProperty(key, value)
  }
  root.style.setProperty('--font-sans', font.stack)
  document.body.style.fontFamily = font.stack

  const meta = document.querySelector('meta[name="theme-color"]')
  if (meta) meta.setAttribute('content', theme.vars['--primary'] ?? '#008f95')
}
