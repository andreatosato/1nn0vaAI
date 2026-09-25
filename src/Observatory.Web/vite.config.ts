import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';
import { createApiProxy } from './server/proxy.ts';

export default defineConfig(({ command, mode }) => {
  const proxy = command === 'serve' ? createApiProxy(loadEnv(mode, process.cwd(), '')) : undefined;
  return {
    plugins: [react()],
    envPrefix: [],
    server: {
      host: '0.0.0.0',
      strictPort: true,
      ...(proxy ? { proxy } : {}),
    },
    preview: {
      host: '0.0.0.0',
      strictPort: true,
      ...(proxy ? { proxy } : {}),
    },
    build: { sourcemap: false },
  };
});
