import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// Dev-proxy: ścieżki /api → backend (profil http, port 5269). Blok `test` typowany dzięki 'vitest/config'.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: 'http://localhost:5269', changeOrigin: true },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/setupTests.ts',
    globals: false,
  },
});
