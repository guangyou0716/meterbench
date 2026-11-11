import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, MeterbenchApi } from './meterbench-api';

describe('MeterbenchApi', () => {
  let api: MeterbenchApi;
  let http: HttpTestingController;

  function setup(base?: string): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ...(base === undefined ? [] : [{ provide: API_BASE_URL, useValue: base }]),
      ],
    });
    api = TestBed.inject(MeterbenchApi);
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  it('calls the documented endpoints on the same origin by default', () => {
    setup();
    api.devices().subscribe();
    api.latest('MTR-A1').subscribe();
    api.history('LC-B1', 30).subscribe();
    api.alarms().subscribe();
    api.alarms(true).subscribe();

    expect(http.expectOne('/api/devices').request.method).toBe('GET');
    http.expectOne('/api/devices/MTR-A1/latest');
    http.expectOne('/api/devices/LC-B1/history?limit=30');
    http.expectOne('/api/alarms');
    http.expectOne('/api/alarms?active=true');
  });

  it('posts tick, reset and acknowledge with no body', () => {
    setup('http://localhost:5080');
    api.tick(10).subscribe();
    api.reset().subscribe();
    api.acknowledge(7).subscribe();

    const tick = http.expectOne('http://localhost:5080/api/simulator/tick?count=10');
    expect(tick.request.method).toBe('POST');
    expect(tick.request.body).toBeNull();
    expect(http.expectOne('http://localhost:5080/api/seed/reset').request.method).toBe('POST');
    expect(http.expectOne('http://localhost:5080/api/alarms/7/ack').request.method).toBe('POST');
  });

  it('encodes device ids in the path', () => {
    setup();
    api.latest('A/B C').subscribe();
    http.expectOne('/api/devices/A%2FB%20C/latest');
  });
});
