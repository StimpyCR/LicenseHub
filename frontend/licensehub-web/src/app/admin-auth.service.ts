import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, of, switchMap, throwError, timeout } from 'rxjs';

export interface AdminSession {
  userId: string;
  email: string;
  mfaVerified: boolean;
  capabilities: string[];
  applicationIds: string[];
}

@Injectable({ providedIn: 'root' })
export class AdminAuthService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/auth';

  session() {
    return this.http.get<AdminSession>(`${this.base}/session`).pipe(
      timeout(10000),
      catchError((error: HttpErrorResponse) =>
        error.status === 401 || error.status === 403 ? of(null) : throwError(() => error),
      ),
    );
  }

  login(email: string, password: string) {
    return this.post<{ mfaRequired: true; challengeExpiresAt: string }>('/login', {
      email,
      password,
    });
  }

  complete(code: string) {
    return this.post<AdminSession>('/mfa', { code });
  }

  logout() {
    return this.post<void>('/logout', {});
  }

  private post<T>(path: string, body: object) {
    // Token asociado a la identidad actual; permanece solamente en memoria.
    return this.http.get<{ token: string }>(`${this.base}/csrf`).pipe(
      switchMap(({ token }) =>
        this.http.post<T>(`${this.base}${path}`, body, {
          headers: new HttpHeaders({ 'X-CSRF-TOKEN': token }),
        }),
      ),
      timeout(10000),
    );
  }
}
