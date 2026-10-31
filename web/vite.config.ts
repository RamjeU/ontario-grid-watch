/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development, Vite serves the UI on :5173 and proxies API and SignalR calls to the
// ASP.NET Core app on :5080. In production the API serves the built files from wwwroot,
// so everything is same-origin.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      '/hubs': { target: 'http://localhost:5080', ws: true },
    },
  },
  build: {
    outDir: '../src/GridWatch.Api/wwwroot',
    emptyOutDir: true,
    // Vendor code changes rarely, so it gets its own long-cached chunks.
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            { name: 'react', test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/ },
            { name: 'signalr', test: /node_modules[\\/]@microsoft[\\/]signalr[\\/]/ },
            { name: 'charts', test: /node_modules[\\/]/ },
          ],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
})
