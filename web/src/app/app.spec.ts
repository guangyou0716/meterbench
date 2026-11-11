import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { App } from './app';
import { MeterbenchApi } from './api/meterbench-api';
import { Alarm, Device, History, Latest } from './api/models';

const devices: Device[] = [
  {
    id: 'MTR-A1',
    name: 'Feeder meter',
    kind: 'electricityMeter',
    location: 'Bay 1',
    quantities: ['voltageL1', 'currentL1'],
  },
  {
    id: 'LC-B1',
    name: 'Line controller',
    kind: 'lineController',
    location: 'Line B',
    quantities: ['cabinetTemperature'],
  },
];

function latestFor(id: string, tick = 3): Latest {
  return {
    deviceId: id,
    tick,
    recordedAt: '2026-01-01T00:03:00+00:00',
    readings: [
      { quantity: 'voltageL1', obis: '9.1.1.1.0.255', unit: 'V', value: 230.1944 },
      { quantity: 'currentL1', obis: '9.1.1.2.0.255', unit: 'A', value: 12.5 },
    ],
  };
}

function historyFor(id: string): History {
  return {
    deviceId: id,
    limit: 30,
    points: [1, 2, 3].map((tick) => ({
      tick,
      recordedAt: 'x',
      readings: [{ quantity: 'voltageL1', obis: 'o', unit: 'V', value: 229 + tick }],
    })),
  };
}

const activeAlarm: Alarm = {
  id: 9,
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
};

class FakeApi {
  alarmList: Alarm[] = [];
  latestResult: () => Observable<Latest> = () => of(latestFor('MTR-A1'));
  devicesResult: () => Observable<Device[]> = () => of(devices);
  devices = vi.fn(() => this.devicesResult());
  latest = vi.fn((_id: string) => this.latestResult());
  history = vi.fn((id: string, _limit: number) => of(historyFor(id)));
  alarms = vi.fn(() => of(this.alarmList));
  acknowledge = vi.fn((id: number) => {
    this.alarmList = this.alarmList.map((a) => (a.id === id ? { ...a, acknowledged: true } : a));
    return of(this.alarmList.find((a) => a.id === id)!);
  });
  tick = vi.fn((count: number) => of({ seed: 1, tick: count, applied: count }));
  reset = vi.fn(() => of({ seed: 1, tick: 0, deviceCount: 2 }));
}

/** The component's async handlers are plain promises, so let them finish before asserting. */
async function settle(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
  await fixture.whenStable();
}

async function render(fake: FakeApi) {
  await TestBed.configureTestingModule({
    imports: [App],
    providers: [{ provide: MeterbenchApi, useValue: fake }],
  }).compileComponents();
  const fixture = TestBed.createComponent(App);
  fixture.autoDetectChanges();
  await settle(fixture);
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

function buttonByText(el: HTMLElement, text: string): HTMLButtonElement {
  const button = Array.from(el.querySelectorAll('button')).find(
    (b) => b.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`No button "${text}"`);
  }
  return button as HTMLButtonElement;
}

describe('App', () => {
  it('lists devices, selects the first, and shows latest values with freshness', async () => {
    const fake = new FakeApi();
    const { el } = await render(fake);

    expect(el.querySelectorAll('nav li').length).toBe(2);
    expect(el.querySelector('nav [aria-pressed="true"]')?.textContent).toContain('MTR-A1');
    expect(fake.latest).toHaveBeenCalledWith('MTR-A1');
    expect(fake.history).toHaveBeenCalledWith('MTR-A1', 30);
    expect(el.textContent).toContain('230.194 V');
    expect(el.querySelector('[data-testid="freshness"]')?.textContent).toContain(
      '2026-01-01 00:03 UTC',
    );
    expect(el.querySelector('.history caption')?.textContent).toContain('Last 3 ticks');
    expect(el.querySelector('svg.spark polyline')?.getAttribute('points')).toBe(
      '0,60 150,30 300,0',
    );
    expect(el.textContent).toContain('No active alarms.');
  });

  it('shows an active alarm and acknowledges it', async () => {
    const fake = new FakeApi();
    fake.alarmList = [activeAlarm];
    const { fixture, el } = await render(fake);

    const banner = el.querySelector('[data-testid="active-alarm"]');
    expect(banner?.textContent).toContain('LC-B1 cabinetTemperature high: 41 > 40 (tick 5)');

    buttonByText(el, 'Acknowledge').click();
    await settle(fixture);

    expect(fake.acknowledge).toHaveBeenCalledWith(9);
    expect(el.querySelector('[data-testid="active-alarm"]')?.textContent).toContain('Acknowledged');
    expect(
      Array.from(el.querySelectorAll('button')).some(
        (b) => b.textContent?.trim() === 'Acknowledge',
      ),
    ).toBe(false);
  });

  it('advances and resets the simulator, then refreshes', async () => {
    const fake = new FakeApi();
    const { fixture, el } = await render(fake);
    const before = fake.alarms.mock.calls.length;

    buttonByText(el, 'Advance 10').click();
    await settle(fixture);
    expect(fake.tick).toHaveBeenCalledWith(10);

    buttonByText(el, 'Reset seed').click();
    await settle(fixture);
    expect(fake.reset).toHaveBeenCalledTimes(1);
    expect(fake.alarms.mock.calls.length).toBe(before + 2);
  });

  it('shows the empty state when a device has no readings yet', async () => {
    const fake = new FakeApi();
    fake.latestResult = () =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            error: { error: { code: 'ReadingNotFound', message: 'none' } },
          }),
      );
    const { el } = await render(fake);

    expect(el.querySelector('[data-testid="no-readings"]')).not.toBeNull();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows an offline banner when the API cannot be reached, and recovers on retry', async () => {
    const fake = new FakeApi();
    fake.devicesResult = () => throwError(() => new HttpErrorResponse({ status: 0 }));
    const { fixture, el } = await render(fake);

    const alert = el.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Offline');
    expect(alert?.textContent).toContain('Cannot reach the Meterbench API');

    fake.devicesResult = () => of(devices);
    buttonByText(el, 'Retry').click();
    await settle(fixture);
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(el.querySelectorAll('nav li').length).toBe(2);
  });

  it('shows API errors other than ReadingNotFound', async () => {
    const fake = new FakeApi();
    fake.latestResult = () =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            error: { error: { code: 'DeviceNotFound', message: "Device 'MTR-A1' was not found." } },
          }),
      );
    const { el } = await render(fake);

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('DeviceNotFound');
  });
});
