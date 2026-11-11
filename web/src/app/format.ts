import { HttpErrorResponse } from '@angular/common/http';
import { AdapterStatus, Alarm, ApiErrorBody, HistoryPoint } from './api/models';

/** Formats a reading with up to three decimals and its unit, e.g. "230.194 V". */
export function formatValue(value: number, unit: string): string {
  if (!Number.isFinite(value)) {
    return `— ${unit}`.trim();
  }
  const text = Number.isInteger(value) ? value.toString() : value.toFixed(3).replace(/\.?0+$/, '');
  return unit ? `${text} ${unit}` : text;
}

/** Synthetic timestamp shown as "2026-01-01 00:10 UTC". Returns the raw text if it is not a date. */
export function formatSyntheticTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return `${date.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

/** "just now", "12 s ago", "3 min ago" relative to `now` (wall clock of the last fetch). */
export function formatAge(fetchedAt: Date, now: Date): string {
  const seconds = Math.max(0, Math.floor((now.getTime() - fetchedAt.getTime()) / 1000));
  if (seconds < 5) {
    return 'just now';
  }
  if (seconds < 60) {
    return `${seconds} s ago`;
  }
  return `${Math.floor(seconds / 60)} min ago`;
}

/** One-line alarm description, e.g. "LC-B1 cabinetTemperature high: 41 > 40 (tick 5)". */
export function describeAlarm(alarm: Alarm): string {
  const op = alarm.direction === 'low' ? '<' : '>';
  return `${alarm.deviceId} ${alarm.quantity} ${alarm.direction}: ${formatValue(alarm.triggerValue, '')} ${op} ${formatValue(alarm.threshold, '')} (tick ${alarm.raisedAtTick})`;
}

/** Active alarms first, then unacknowledged, then newest. */
export function sortAlarms(alarms: readonly Alarm[]): Alarm[] {
  return [...alarms].sort(
    (a, b) =>
      Number(b.active) - Number(a.active) ||
      Number(a.acknowledged) - Number(b.acknowledged) ||
      b.raisedAtTick - a.raisedAtTick ||
      b.id - a.id,
  );
}

/** Values of one quantity across history points, oldest first, for the table and sparkline. */
export function seriesFor(
  points: readonly HistoryPoint[],
  quantity: string,
): { tick: number; value: number }[] {
  return points
    .flatMap((point) => {
      const reading = point.readings.find((r) => r.quantity === quantity);
      return reading ? [{ tick: point.tick, value: reading.value }] : [];
    })
    .sort((a, b) => a.tick - b.tick);
}

/** SVG polyline points for a sparkline in a width x height box. Empty for fewer than 2 values. */
export function sparklinePoints(values: readonly number[], width: number, height: number): string {
  if (values.length < 2) {
    return '';
  }
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;
  const step = width / (values.length - 1);
  return values
    .map((v, i) => {
      const x = i * step;
      const y = height - ((v - min) / span) * height;
      return `${round1(x)},${round1(y)}`;
    })
    .join(' ');
}

function round1(n: number): number {
  return Math.round(n * 10) / 10;
}

export type ProblemKind = 'offline' | 'api' | 'unknown';

export interface Problem {
  kind: ProblemKind;
  message: string;
}

/** Maps an HTTP failure to what the UI shows: offline (no response), API error body, or generic. */
export function toProblem(error: unknown): Problem {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return { kind: 'offline', message: 'Cannot reach the Meterbench API. Is it running?' };
    }
    const body = error.error as Partial<ApiErrorBody> | null;
    if (body && typeof body === 'object' && body.error && typeof body.error.message === 'string') {
      return { kind: 'api', message: `${body.error.code}: ${body.error.message}` };
    }
    return { kind: 'api', message: `HTTP ${error.status} ${error.statusText}`.trim() };
  }
  return { kind: 'unknown', message: 'Unexpected error.' };
}

export type AdapterTone = 'off' | 'ok' | 'warn';

export interface AdapterLabel {
  name: string;
  text: string;
  tone: AdapterTone;
}

/** Short labels for the header: one per adapter, with a tone for colour. */
export function describeAdapters(status: AdapterStatus): AdapterLabel[] {
  const { modbus, mqtt } = status;
  const modbusLabel: AdapterLabel = !modbus.enabled
    ? { name: 'Modbus', text: 'off', tone: 'off' }
    : modbus.listening
      ? { name: 'Modbus', text: `listening on :${modbus.port ?? '?'}`, tone: 'ok' }
      : { name: 'Modbus', text: 'not listening', tone: 'warn' };
  const ticks = `${mqtt.publishedTicks} tick${mqtt.publishedTicks === 1 ? '' : 's'} published`;
  const mqttLabel: AdapterLabel = !mqtt.enabled
    ? { name: 'MQTT', text: 'off', tone: 'off' }
    : mqtt.connected
      ? { name: 'MQTT', text: `connected to ${mqtt.broker ?? '?'}, ${ticks}`, tone: 'ok' }
      : { name: 'MQTT', text: `not connected to ${mqtt.broker ?? '?'}, ${ticks}`, tone: 'warn' };
  return [modbusLabel, mqttLabel];
}
