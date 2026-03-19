import { ref, watch } from 'vue'

export type ThemePreference = 'system' | 'light' | 'dark'

const STORAGE_KEY = 'theme-preference'

const preference = ref<ThemePreference>(load())
const systemDark = matchMedia('(prefers-color-scheme: dark)')

function load(): ThemePreference {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'light' || stored === 'dark') return stored
  return 'system'
}

function apply() {
  const dark = preference.value === 'dark'
    || (preference.value === 'system' && systemDark.matches)
  document.documentElement.classList.toggle('dark', dark)
}

systemDark.addEventListener('change', apply)

watch(preference, (value) => {
  if (value === 'system') localStorage.removeItem(STORAGE_KEY)
  else localStorage.setItem(STORAGE_KEY, value)
  apply()
})

apply()

export function useTheme() {
  return { preference }
}
