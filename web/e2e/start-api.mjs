// Starts the Meterbench API for the e2e run with a throwaway SQLite database.
import { spawn } from 'node:child_process';
import { mkdirSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const port = process.argv[2] ?? '5080';
const here = dirname(fileURLToPath(import.meta.url));
const dataDir = join(here, '.data');
const project = resolve(here, '..', '..', 'src', 'Meterbench.Api');

rmSync(dataDir, { recursive: true, force: true });
mkdirSync(dataDir, { recursive: true });

const child = spawn(
  'dotnet',
  ['run', '--project', project, '--no-launch-profile', '--urls', `http://127.0.0.1:${port}`],
  {
    stdio: 'inherit',
    env: {
      ...process.env,
      ConnectionStrings__Meterbench: `Data Source=${join(dataDir, 'meterbench.db')}`,
    },
  },
);

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => child.kill(signal));
}
child.on('exit', (code) => process.exit(code ?? 1));
