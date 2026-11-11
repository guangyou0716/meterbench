# Meterbench web dashboard

Angular + TypeScript dashboard for the Meterbench API. All data is synthetic.

Requires Node 22.22+ or 24.15+.

```bash
# terminal 1: the API on http://localhost:5080
dotnet run --project src/Meterbench.Api --urls http://localhost:5080

# terminal 2: the dashboard on http://localhost:4200 (proxies /api to :5080)
cd web
npm ci
npm start
```

What it shows: device list, latest values with tick, synthetic time and fetch age, a history table and sparkline for one quantity, active alarms with Acknowledge, an alarm log, and empty, error and offline states. Simulator controls: advance 1 or 10 ticks, auto-run (one tick every 2 s), reset seed.

Checks: `npm run check` runs Prettier, TypeScript (app and specs), Vitest unit tests, and a production build.
