import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, InjectionToken, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Alarm, Device, History, Latest, SeedReset, TickResult } from './models';

/** Base URL of the API. Empty means same origin (ng serve proxies /api to the .NET API). */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});

@Injectable({ providedIn: 'root' })
export class MeterbenchApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  devices(): Observable<Device[]> {
    return this.http.get<Device[]>(`${this.base}/api/devices`);
  }

  latest(deviceId: string): Observable<Latest> {
    return this.http.get<Latest>(`${this.base}/api/devices/${encodeURIComponent(deviceId)}/latest`);
  }

  history(deviceId: string, limit: number): Observable<History> {
    const params = new HttpParams().set('limit', limit);
    return this.http.get<History>(
      `${this.base}/api/devices/${encodeURIComponent(deviceId)}/history`,
      { params },
    );
  }

  alarms(active?: boolean): Observable<Alarm[]> {
    let params = new HttpParams();
    if (active !== undefined) {
      params = params.set('active', active);
    }
    return this.http.get<Alarm[]>(`${this.base}/api/alarms`, { params });
  }

  acknowledge(id: number): Observable<Alarm> {
    return this.http.post<Alarm>(`${this.base}/api/alarms/${id}/ack`, null);
  }

  tick(count: number): Observable<TickResult> {
    const params = new HttpParams().set('count', count);
    return this.http.post<TickResult>(`${this.base}/api/simulator/tick`, null, { params });
  }

  reset(): Observable<SeedReset> {
    return this.http.post<SeedReset>(`${this.base}/api/seed/reset`, null);
  }
}
