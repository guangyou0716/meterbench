# Invented register map

This map was written for the Meterbench simulator. It is not a vendor manual, not a workplace register list, and not a certified DLMS/COSEM or OBIS object model. Medium `9` in the codes below is a fictional group used only by this repo.

Do not point this API at a real meter.

## Fleet

Seed `20261111`. Three devices:

| Id | Kind | Name | Location |
| --- | --- | --- | --- |
| `MTR-A1` | `electricityMeter` | Electricity meter A1 | Bay 1 / Line A |
| `MTR-A2` | `electricityMeter` | Electricity meter A2 | Bay 1 / Line A |
| `LC-B1` | `lineController` | Line controller B1 | Bay 1 / Line B |

Locations are labels for the demo. They are not a site.

## How a reading is produced

`ReadingSimulator` is in-process. It does not open a socket.

- Synthetic time is `2026-01-01T00:00:00Z` plus one minute per tick.
- A random-walk channel is a pure function of `(seed, device id, quantity, tick)`. The walk is recomputed from the baseline on every tick. The stored row is not the next input.
- The generator is xorshift32. Its seed is FNV-1a over the little-endian seed, the device id, and the quantity code. Each step adds `(u - 0.5) * 2 * step`, where `u` is in `[0, 1)`, then clamps to min/max. The result is rounded to 3 decimal places, midpoint away from zero.
- Cumulative energy is not a physical integral of power. It is `baseline + tick * step`.
- Cabinet temperature ignores the walk and follows the script in the table below so alarm tests land on known ticks.

`MTR-A2` uses the same quantity layout as `MTR-A1` with different baselines, listed in parentheses.

## Electricity meters

Holding addresses are invented placeholders for a later Modbus increment. Nothing serves them in v0.1. Scale is the tutorial-frame exponent: engineering = scaled int32 * 10^scale.

| Code | OBIS-like | Unit | Scale | Holding | Baseline A1 (A2) | Step | Min | Max | Low | High | Hysteresis | Script |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `voltageL1` | `9.1.1.1.0.255` | V | -1 | 0 | 230 (229.5) | 0.35 | 210 | 250 | 216 | 244 | 1 | random walk |
| `currentL1` | `9.1.1.2.0.255` | A | -2 | 2 | 8 (5.5) | 0.25 | 0 | 80 | | 60 | 1 | random walk |
| `activePowerKw` | `9.1.1.3.0.255` | kW | -3 | 4 | 1.84 (1.26) | 0.05 | 0 | 25 | | 20 | 0.2 | random walk |
| `frequencyHz` | `9.1.1.4.0.255` | Hz | -2 | 6 | 50 (50) | 0.01 | 49 | 51 | 49.5 | 50.5 | 0.05 | random walk |
| `activeEnergyKwh` | `9.1.1.5.0.255` | kWh | -2 | 8 | 1000 (2500) | 0.25 | 0 | 1000000 | | | 0 | cumulative |

A blank threshold means that side is not armed.

## Line controller `LC-B1`

| Code | OBIS-like | Unit | Scale | Holding | Baseline | Step | Min | Max | Low | High | Hysteresis | Script |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `lineVoltage` | `9.2.1.1.0.255` | V | -1 | 0 | 400 | 0.5 | 360 | 440 | 380 | 420 | 2 | random walk |
| `loadCurrent` | `9.2.1.2.0.255` | A | -2 | 2 | 12 | 0.3 | 0 | 60 | | 40 | 1 | random walk |
| `cabinetTemperature` | `9.2.1.3.0.255` | C | -1 | 4 | 25 | | 0 | 80 | | 40 | 1.5 | script |

`C` means degrees Celsius.

### Cabinet temperature script

| Tick | °C | Alarm |
| --- | --- | --- |
| 0 | 25.0 | inside |
| 1 | 25.1 | inside |
| 2 | 25.2 | inside |
| 3 | 25.3 | inside |
| 4 | 25.4 | inside |
| 5 | 41.0 | raise high (`41 > 40`) |
| 6 | 42.5 | still active, no second raise |
| 7 | 39.0 | still active (`39 > 38.5`) |
| 8 | 38.5 | clear (`38.5 <= 40 - 1.5`) |
| 9 and later | 25.0 | idle |

## Alarm rule

For each device and quantity there is at most one active alarm.

- High raise: `value > high`. Low raise: `value < low`. A value sitting on the threshold does not raise.
- While it is active, a worse reading does not create a second alarm.
- High clear: `value <= high - hysteresis`. Low clear: `value >= low + hysteresis`.
- Acknowledge sets a timestamp and leaves the alarm active. A second acknowledge keeps the original timestamp. After the alarm clears, a new breach can raise a new row.

## Unofficial frame

Not DLMS/COSEM and not HDLC. The byte layout is a tutorial stand-in so a parser can be tested without a meter.

```
0x7E | length | payload | CRC-16/X-25 (little-endian) | 0x7E
```

- `length` is the payload size only, one byte.
- CRC-16/X-25 covers the length byte and the payload (not the flags). Reflected poly `0x8408`, init `0xFFFF`, xorout `0xFFFF`. The check value for ASCII `123456789` is `0x906E`.
- Payload: device-id length (`1..16`), printable ASCII device id, six OBIS-like bytes, int8 scale, int32 big-endian scaled value.
- Engineering value = scaled int32 * 10^scale. Example: device `MTR-A1`, code `9.1.1.1.0.255`, scale `-1`, scaled value `2301` means `230.1` V.

The parser returns a typed success or one typed error: bad start, bad end, length mismatch, CRC mismatch, or truncated. The HTTP API does not expose a frame route in this increment; `FrameCodec` is the library.
