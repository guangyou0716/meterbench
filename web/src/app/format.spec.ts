import { HttpErrorResponse } from '@angular/common/http';
import { Alarm, HistoryPoint } from './api/models';
import {
  describeAdapters,
  describeAlarm,
  formatAge,
  formatSyntheticTime,
  formatValue,
  seriesFor,
  sortAlarms,
  sparklinePoints,
  toProblem,
} from './format';

function alarm(overrides: Partial<Alarm>): Alarm {
  return {
    id: 1,
    deviceId: 'LC-B1',
    quantity: 'cabinetTemperature',
    direction: 'high',
    threshold: 40,
    hysteresis: 1.5,
    triggerValue: 41,
    clearValue: null,
    raisedAtTick: 5,
    clearedAtTick: null,
    active: true,
    acknowledged: false,
    acknowledgedAt: null,
    ...overrides,
  };
}

describe('formatValue', () => {
  it('trims trailing zeros and keeps three decimals at most', () => {
    expect(formatValue(230.19412, 'V')).toBe('230.194 V');
    expect(formatValue(41.5, '°C')).toBe('41.5 °C');
    expect(formatValue(50, 'Hz')).toBe('50 Hz');
    expect(formatValue(0.0004, 'A')).toBe('0 A');
  });

  it('handles missing unit and non-finite values', () => {
    expect(formatValue(12.25, '')).toBe('12.25');
    expect(formatValue(Number.NaN, 'V')).toBe('— V');
    expect(formatValue(Number.POSITIVE_INFINITY, '')).toBe('—');
  });
});

describe('formatSyntheticTime', () => {
  it('shows minutes in UTC', () => {
    expect(formatSyntheticTime('2026-01-01T00:10:00+00:00')).toBe('2026-01-01 00:10 UTC');
  });

  it('returns raw text when not a date', () => {
    expect(formatSyntheticTime('nope')).toBe('nope');
  });
});

describe('formatAge', () => {
  const at = new Date('2026-11-11T12:00:00Z');
  it.each([
    [0, 'just now'],
    [4_999, 'just now'],
    [12_000, '12 s ago'],
    [185_000, '3 min ago'],
    [-5_000, 'just now'],
  ])('%i ms later is %s', (ms, text) => {
    expect(formatAge(at, new Date(at.getTime() + ms))).toBe(text);
  });
});

describe('describeAlarm', () => {
  it('uses > for high and < for low', () => {
    expect(describeAlarm(alarm({}))).toBe('LC-B1 cabinetTemperature high: 41 > 40 (tick 5)');
    expect(
      describeAlarm(
        alarm({ direction: 'low', triggerValue: 209.5, threshold: 210, raisedAtTick: 9 }),
      ),
    ).toBe('LC-B1 cabinetTemperature low: 209.5 < 210 (tick 9)');
  });
});

describe('sortAlarms', () => {
  it('puts active, then unacknowledged, then newest first without mutating input', () => {
    const input = [
      alarm({ id: 1, active: false, raisedAtTick: 50 }),
      alarm({ id: 2, acknowledged: true, raisedAtTick: 40 }),
      alarm({ id: 3, raisedAtTick: 10 }),
      alarm({ id: 4, raisedAtTick: 30 }),
    ];
    expect(sortAlarms(input).map((a) => a.id)).toEqual([4, 3, 2, 1]);
    expect(input.map((a) => a.id)).toEqual([1, 2, 3, 4]);
  });
});

describe('seriesFor', () => {
  const points: HistoryPoint[] = [
    {
      tick: 3,
      recordedAt: 'x',
      readings: [{ quantity: 'v', obis: 'o', unit: 'V', value: 3 }],
    },
    { tick: 1, recordedAt: 'x', readings: [{ quantity: 'v', obis: 'o', unit: 'V', value: 1 }] },
    { tick: 2, recordedAt: 'x', readings: [{ quantity: 'i', obis: 'o', unit: 'A', value: 9 }] },
  ];

  it('returns oldest-first values for one quantity and skips points without it', () => {
    expect(seriesFor(points, 'v')).toEqual([
      { tick: 1, value: 1 },
      { tick: 3, value: 3 },
    ]);
    expect(seriesFor(points, 'missing')).toEqual([]);
  });
});

describe('sparklinePoints', () => {
  it('needs two values', () => {
    expect(sparklinePoints([], 100, 10)).toBe('');
    expect(sparklinePoints([5], 100, 10)).toBe('');
  });

  it('scales min to the bottom and max to the top', () => {
    expect(sparklinePoints([0, 5, 10], 100, 10)).toBe('0,10 50,5 100,0');
  });

  it('draws a flat series at the bottom instead of dividing by zero', () => {
    expect(sparklinePoints([7, 7], 100, 10)).toBe('0,10 100,10');
  });
});

describe('toProblem', () => {
  it('treats status 0 as offline', () => {
    expect(toProblem(new HttpErrorResponse({ status: 0 })).kind).toBe('offline');
  });

  it('uses the API error body when present', () => {
    const error = new HttpErrorResponse({
      status: 404,
      error: { error: { code: 'DeviceNotFound', message: "Device 'X' was not found." } },
    });
    expect(toProblem(error)).toEqual({
      kind: 'api',
      message: "DeviceNotFound: Device 'X' was not found.",
    });
  });

  it('falls back to the HTTP status, then to a generic message', () => {
    expect(toProblem(new HttpErrorResponse({ status: 500, statusText: 'Server Error' }))).toEqual({
      kind: 'api',
      message: 'HTTP 500 Server Error',
    });
    expect(toProblem(new Error('x')).kind).toBe('unknown');
  });
});

describe('describeAdapters', () => {
  const off = {
    modbus: { enabled: false, listening: false, port: null },
    mqtt: { enabled: false, connected: false, broker: null, topicPrefix: null, publishedTicks: 0 },
  };

  it('labels disabled adapters as off', () => {
    expect(describeAdapters(off)).toEqual([
      { name: 'Modbus', text: 'off', tone: 'off' },
      { name: 'MQTT', text: 'off', tone: 'off' },
    ]);
  });

  it('labels a listening Modbus simulator and a connected MQTT publisher as ok', () => {
    const labels = describeAdapters({
      modbus: { enabled: true, listening: true, port: 5020 },
      mqtt: {
        enabled: true,
        connected: true,
        broker: '127.0.0.1:1883',
        topicPrefix: 'meterbench',
        publishedTicks: 1,
      },
    });
    expect(labels).toEqual([
      { name: 'Modbus', text: 'listening on :5020', tone: 'ok' },
      { name: 'MQTT', text: 'connected to 127.0.0.1:1883, 1 tick published', tone: 'ok' },
    ]);
  });

  it('warns when an enabled adapter is not up', () => {
    const labels = describeAdapters({
      modbus: { enabled: true, listening: false, port: null },
      mqtt: {
        enabled: true,
        connected: false,
        broker: '127.0.0.1:1883',
        topicPrefix: 'meterbench',
        publishedTicks: 4,
      },
    });
    expect(labels.map((l) => l.tone)).toEqual(['warn', 'warn']);
    expect(labels[0].text).toBe('not listening');
    expect(labels[1].text).toBe('not connected to 127.0.0.1:1883, 4 ticks published');
  });
});
