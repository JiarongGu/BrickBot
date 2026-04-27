import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import viteTsconfigPaths from 'vite-tsconfig-paths';
import checker from 'vite-plugin-checker';
import path from 'path';

// Output goes into ../BrickBot/wwwroot which the C# project embeds at build time.
export default defineConfig({
  base: 'https://app.local/',
  plugins: [
    react(),
    viteTsconfigPaths(),
    checker({
      typescript: true,
      overlay: { initialIsOpen: false, position: 'br' },
      enableBuild: false,
    }),
  ],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: { port: 3000, open: false },
  build: {
    outDir: '../BrickBot/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    minify: 'esbuild',
    target: 'esnext',
    rollupOptions: {
      output: {
        manualChunks: {
          'antd': ['antd'],
          'react-vendor': ['react', 'react-dom'],
          'i18n': ['i18next', 'react-i18next'],
          'monaco': ['@monaco-editor/react'],
        },
      },
    },
  },
  css: {
    modules: { localsConvention: 'camelCase' },
  },
});
