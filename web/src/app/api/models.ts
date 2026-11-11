/** Wire types for the Meterbench API (camelCase JSON). Mirrors src/Meterbench.Api/Contracts. */

export interface Device {
  id: string;
  name: string;
  kind: string;
  location: string;
  quantities: string[];
}

export interface QuantityReading {
  quantity: string;
  obis: string;
  unit: string;
  value: number;
}

export interface Latest {
  deviceId: string;
  tick: number;
  recordedAt: string;
  readings: QuantityReading[];
}

export interface HistoryPoint {
  tick: number;
  recordedAt: string;
  readings: QuantityReading[];
}

export interface History {
  deviceId: string;
  limit: number;
  points: HistoryPoint[];
}

export interface Alarm {
  id: number;
  deviceId: string;
  quantity: string;
  direction: 'high' | 'low' | string;
  threshold: number;
  hysteresis: number;
  triggerValue: number;
  clearValue: number | null;
  raisedAtTick: number;
  clearedAtTick: number | null;
  active: boolean;
  acknowledged: boolean;
  acknowledgedAt: string | null;
}

export interface TickResult {
  seed: number;
  tick: number;
  applied: number;
}

export interface SeedReset {
  seed: number;
  tick: number;
  deviceCount: number;
}

export interface ApiErrorBody {
  error: { code: string; message: string };
}
