import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, map, of, timeout } from 'rxjs';

export type Availability = 'checking' | 'ready' | 'unavailable';

@Injectable({ providedIn: 'root' })
export class AvailabilityService {
  private readonly http = inject(HttpClient);
  check() {
    return this.http.get('/health/ready', { responseType: 'text' }).pipe(
      timeout(5000),
      map((result): Availability => (result === 'Healthy' ? 'ready' : 'unavailable')),
      catchError(() => of<Availability>('unavailable')),
    );
  }
}
