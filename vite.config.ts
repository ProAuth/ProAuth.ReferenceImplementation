import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const app = process.env.MERVIKON_APP || 'Workplace';
if (!['Workplace', 'SupplierPortal', 'FinanceDesk', 'DemoConsole'].includes(app))
  throw new Error('Unknown frontend');
export default defineConfig({
  root: `src/apps/${app}`,
  plugins: [react()],
  build: {
    outDir:
      app === 'DemoConsole' ? '../../demo-console/DemoConsole/wwwroot' : `../${app}.Bff/wwwroot`,
    emptyOutDir: true,
  },
});
