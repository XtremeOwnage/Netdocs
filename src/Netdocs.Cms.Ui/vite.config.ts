import { defineConfig } from 'vite';
import preact from '@preact/preset-vite';
import { resolve } from 'node:path';

// Two bundles: the admin app (served at /__cms/) and the in-page editor that `netdocs serve`
// adds to every page. File names carry no hash: the CLI embeds them under fixed names.
export default defineConfig({
  plugins: [preact()],
  base: '/__cms/',
  build: {
    outDir: resolve(__dirname, '../Netdocs.Cli/Cms/wwwroot'),
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    chunkSizeWarningLimit: 2000,
    rollupOptions: {
      input: {
        index: resolve(__dirname, 'index.html'),
        inline: resolve(__dirname, 'src/inline/inline.ts'),
      },
      output: {
        entryFileNames: '[name].js',
        chunkFileNames: 'chunks/[name].js',
        assetFileNames: '[name][extname]',
      },
    },
  },
});
