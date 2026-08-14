import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['test/unit/**/*.test.ts', 'test/integration/**/*.test.ts'],
    environment: 'node',
    alias: {
      vscode: fileURLToPath(
        new URL('./test/unit/__mocks__/vscode.ts', import.meta.url),
      ),
    },
  },
});
