import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  // shadcn/ui primitives export cva variants alongside components by design;
  // Fast Refresh's components-only rule conflicts with that contract.
  // ThemeContext colocates Provider + useTheme for the same one-import ergonomics.
  {
    files: ['src/components/ui/**/*.tsx', 'src/contexts/**/*.tsx'],
    rules: { 'react-refresh/only-export-components': 'off' },
  },
  // The realtime hub hook owns an external connection state machine and must
  // publish "Connecting" inside the effect — that is the effect's purpose.
  {
    files: ['src/hooks/useFlareHub.ts'],
    rules: { 'react-hooks/set-state-in-effect': 'off' },
  },
])
