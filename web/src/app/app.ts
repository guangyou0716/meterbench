import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MeterbenchApi } from './api/meterbench-api';
import { Alarm, Device, HistoryPoint, Latest } from './api/models';
import {
  Problem,
  describeAlarm,
  formatAge,
  formatSyntheticTime,
  formatValue,
  seriesFor,
  sortAlarms,
  sparklinePoints,
  toProblem,
} from './format';

/** History points requested per refresh. The API allows 1..500. */
export const HISTORY_LIMIT = 30;
/** Auto-run advances the simulator by one tick at this interval. */
export const AUTO_TICK_MS = 2000;

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit, OnDestroy {
  private readonly api = inject(MeterbenchApi);
  private autoTimer: ReturnType<typeof setInterval> | null = null;
  private clockTimer: ReturnType<typeof setInterval> | null = null;

  protected readonly devices = signal<Device[] | null>(null);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly latest = signal<Latest | null>(null);
  protected readonly history = signal<HistoryPoint[]>([]);
  protected readonly selectedQuantity = signal<string | null>(null);
  protected readonly alarms = signal<Alarm[]>([]);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly busy = signal(false);
  protected readonly autoRun = signal(false);
  protected readonly fetchedAt = signal<Date | null>(null);
  protected readonly now = signal(new Date());

  protected readonly selectedDevice = computed(
    () => this.devices()?.find((d) => d.id === this.selectedId()) ?? null,
  );
  protected readonly activeAlarms = computed(() =>
    sortAlarms(this.alarms().filter((a) => a.active)),
  );
  protected readonly recentAlarms = computed(() => sortAlarms(this.alarms()).slice(0, 10));
  protected readonly series = computed(() => {
    const quantity = this.selectedQuantity();
    return quantity ? seriesFor(this.history(), quantity) : [];
  });
  protected readonly sparkline = computed(() =>
    sparklinePoints(
      this.series().map((p) => p.value),
      300,
      60,
    ),
  );
  protected readonly selectedUnit = computed(() => {
    const quantity = this.selectedQuantity();
    return this.latest()?.readings.find((r) => r.quantity === quantity)?.unit ?? '';
  });
  protected readonly age = computed(() => {
    const fetched = this.fetchedAt();
    return fetched ? formatAge(fetched, this.now()) : 'never';
  });

  protected readonly formatValue = formatValue;
  protected readonly formatSyntheticTime = formatSyntheticTime;
  protected readonly describeAlarm = describeAlarm;

  async ngOnInit(): Promise<void> {
    this.clockTimer = setInterval(() => this.now.set(new Date()), 1000);
    await this.loadDevices();
  }

  ngOnDestroy(): void {
    this.stopAuto();
    if (this.clockTimer !== null) {
      clearInterval(this.clockTimer);
      this.clockTimer = null;
    }
  }

  async loadDevices(): Promise<void> {
    try {
      const devices = await firstValueFrom(this.api.devices());
      this.devices.set(devices);
      this.problem.set(null);
      if (devices.length > 0 && !devices.some((d) => d.id === this.selectedId())) {
        await this.select(devices[0]);
        return;
      }
      await this.refresh();
    } catch (error) {
      this.problem.set(toProblem(error));
    }
  }

  async select(device: Device): Promise<void> {
    this.selectedId.set(device.id);
    if (!device.quantities.includes(this.selectedQuantity() ?? '')) {
      this.selectedQuantity.set(device.quantities[0] ?? null);
    }
    await this.refresh();
  }

  selectQuantity(quantity: string): void {
    this.selectedQuantity.set(quantity);
  }

  /** Reloads latest values, history and alarms for the selected device. */
  async refresh(): Promise<void> {
    const id = this.selectedId();
    try {
      const [latest, history, alarms] = await Promise.all([
        id ? this.loadLatest(id) : Promise.resolve(null),
        id ? firstValueFrom(this.api.history(id, HISTORY_LIMIT)) : Promise.resolve(null),
        firstValueFrom(this.api.alarms()),
      ]);
      this.latest.set(latest);
      this.history.set(history?.points ?? []);
      this.alarms.set(alarms);
      this.fetchedAt.set(new Date());
      this.problem.set(null);
    } catch (error) {
      this.problem.set(toProblem(error));
    }
  }

  async tick(count: number): Promise<void> {
    await this.run(() => firstValueFrom(this.api.tick(count)));
  }

  async reset(): Promise<void> {
    await this.run(() => firstValueFrom(this.api.reset()));
  }

  async acknowledge(alarm: Alarm): Promise<void> {
    await this.run(() => firstValueFrom(this.api.acknowledge(alarm.id)));
  }

  toggleAuto(): void {
    if (this.autoRun()) {
      this.stopAuto();
      return;
    }
    this.autoRun.set(true);
    this.autoTimer = setInterval(() => {
      if (!this.busy()) {
        void this.tick(1);
      }
    }, AUTO_TICK_MS);
  }

  private stopAuto(): void {
    this.autoRun.set(false);
    if (this.autoTimer !== null) {
      clearInterval(this.autoTimer);
      this.autoTimer = null;
    }
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    try {
      await action();
      await this.refresh();
    } catch (error) {
      this.problem.set(toProblem(error));
      if (this.problem()?.kind === 'offline') {
        this.stopAuto();
      }
    } finally {
      this.busy.set(false);
    }
  }

  /** A device with no readings yet answers 404 ReadingNotFound; show that as an empty state. */
  private async loadLatest(id: string): Promise<Latest | null> {
    try {
      return await firstValueFrom(this.api.latest(id));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        const code = (error.error as { error?: { code?: string } } | null)?.error?.code;
        if (code === 'ReadingNotFound') {
          return null;
        }
      }
      throw error;
    }
  }
}
