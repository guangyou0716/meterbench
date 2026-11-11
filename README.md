# Meterbench

Synthetic industrial telemetry workbench. Three invented devices walk deterministic readings, a small alarm engine raises and clears thresholds, and an ASP.NET Core API stores the result in SQLite.

This is a simulator. It is not affiliated with LHDN, PayNet, meter vendors, or former employers. It is not certified DLMS/COSEM.

All device ids, locations, register codes, and numbers are invented. See [docs/register-map.md](docs/register-map.md). There is no plant data and no secrets in this tree.

Increment 1 is the backend. An Angular dashboard is a later increment and is not in this repository yet.

## Architecture

```
seed 20261111 + tick
        |
        v
ReadingSimulator (in process, no socket)
  random walk | cumulative energy | scripted temperature
        |
        +----> FrameCodec (unofficial DLMS-like bytes; library only in v0.1)
        |
        v
AlarmEngine
  high / low thresholds, hysteresis clear, acknowledge
        |
        v
Meterbench.Api
  EF Core + SQLite (meterbench.db)
  GET  /api/devices
  GET  /api/devices/{id}/latest
  GET  /api/devices/{id}/history?limit=
  GET  /api/alarms?active=
  POST /api/alarms/{id}/ack
  POST /api/simulator/tick?count=
  POST /api/seed/reset
        |
        v
(later) Angular dashboard
(later) Modbus TCP and MQTT — interfaces only in v0.1, no listener and no broker
```

`Meterbench.Core` holds the fleet catalog, simulator, frame codec, and alarm engine. `Meterbench.Api` is the HTTP and SQLite edge. `Meterbench.Tests` covers the codec, the alarm rules, and the API through `WebApplicationFactory`.

## Run

Requires the .NET 8 SDK. The same commands work on Linux and Windows once `dotnet` is on `PATH`.

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/Meterbench.Api --urls http://localhost:5080
```

The API listens on `http://localhost:5080` and creates `meterbench.db` in the working directory (gitignored). Delete that file if the model changes; startup uses `EnsureCreated` and does not run migrations.

```bash
curl -s http://localhost:5080/api/devices
curl -s http://localhost:5080/api/devices/MTR-A1/latest
curl -s "http://localhost:5080/api/devices/MTR-A1/history?limit=10"
curl -s -X POST "http://localhost:5080/api/simulator/tick?count=5"
curl -s "http://localhost:5080/api/alarms?active=true"
curl -s -X POST http://localhost:5080/api/alarms/1/ack
curl -s -X POST http://localhost:5080/api/seed/reset
```

`POST /api/seed/reset` reloads seed `20261111` and tick 0. The same seed and the same tick count produce the same readings. Synthetic time starts at `2026-01-01T00:00:00Z` and advances one minute per tick.

## API

Successful bodies are JSON. Errors are `400` or `404` with this body and no other fields:

```json
{ "error": { "code": "DeviceNotFound", "message": "Device 'NO-SUCH' was not found." } }
```

| Method | Path | Success | Error |
| --- | --- | --- | --- |
| GET | `/api/devices` | `200` fleet: `MTR-A1`, `MTR-A2`, `LC-B1` | |
| GET | `/api/devices/{id}/latest` | `200` readings at the newest tick | `404` `DeviceNotFound`, `404` `ReadingNotFound` |
| GET | `/api/devices/{id}/history?limit=N` | `200` newest ticks first. `limit` is `1..500`, default `100` | `400` `InvalidLimit`, `404` `DeviceNotFound` |
| GET | `/api/alarms?active=true\|false` | `200` alarms. Omit `active` for all | `400` `InvalidActive` |
| POST | `/api/alarms/{id}/ack` | `200` alarm with `acknowledged: true`. Repeating ack keeps the first timestamp | `400` `InvalidAlarmId`, `404` `AlarmNotFound` |
| POST | `/api/simulator/tick?count=N` | `200` `{ seed, tick, applied }`. `count` is `1..1000` | `400` `InvalidCount` |
| POST | `/api/seed/reset` | `200` `{ seed: 20261111, tick: 0, deviceCount: 3 }` | |

Validation runs before the device lookup, so a bad `limit` is `400` even when the id is unknown.

Alarm rules, per device and quantity:

- Raise high when `value > high`, low when `value < low`.
- While that alarm is active, another raise is not created.
- Clear high when `value <= high - hysteresis`, low when `value >= low + hysteresis`.
- Acknowledge does not clear the alarm.

`LC-B1` cabinet temperature is scripted so this is visible without waiting on the random walk: tick 5 raises at 41°C (high 40), ticks 6 and 7 stay active, tick 8 clears at 38.5°C (hysteresis 1.5). Details are in the register map.

## Tests

```bash
dotnet test -c Release
```

Parser tests cover a valid frame, round-trip encode/parse, and each typed error: bad start, bad end, length mismatch, CRC mismatch, truncated. Alarm tests cover raise, hysteresis clear, acknowledge, and no duplicate raise. API tests use `WebApplicationFactory` against a temporary SQLite file created for that test. They do not open a socket, do not use the network, and do not share the demo `meterbench.db`.

CI is [`.github/workflows/ci.yml`](.github/workflows/ci.yml): `ubuntu-latest`, .NET `8.0.x`, restore, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build`.

## What this is not

- Not a connection to a real meter, PLC, factory network, or customer site.
- Not certified DLMS/COSEM, IEC 62056, or legal metrology. The frame codec is an unofficial tutorial layout (start flag, length, payload, CRC-16/X-25, end flag).
- Not affiliated with LHDN, PayNet, meter vendors, or former employers.
- Not energy billing, settlement, or safety-critical control. Nothing writes coils or commands a machine.
- Not Modbus or MQTT yet. Those seams are interfaces only; v0.1 does not listen or publish.
- Not a SQL Server operations claim. SQLite is the local demo database.
- Not a place for employer data, register maps from a workplace, or secrets.

## License

MIT. Copyright (c) 2026 guangyou0716. See [LICENSE](LICENSE).
