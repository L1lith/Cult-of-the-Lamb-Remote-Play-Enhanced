import { defineConfig } from 'astro/config';
import solid from '@astrojs/solid-js';

export default defineConfig({
  integrations: [solid()],
  output: 'static',
  outDir: './dist',
  vite: {
    clearScreen: false,
    server: {
      port: 4321,
      strictPort: true,
    },
  },
});
