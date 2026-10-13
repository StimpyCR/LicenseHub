import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { finalize, timeout } from 'rxjs';

interface Dashboard {
  evaluatedAt: string;
  activeLicenses: number;
  pendingLicenses: number;
  expiringWithinSevenDays: number;
  customers: number;
}
interface Audit {
  id: string;
  action: string;
  actorId: string;
  reason: string | null;
  createdAt: string;
  entityId: string | null;
  traceId: string;
}
@Component({
  selector: 'app-admin-reports',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h4>Estadísticas</h4>
    <button mat-button [disabled]="busy()" (click)="loadDashboard()">
      Actualizar estadísticas
    </button>
    @if (dashboard(); as data) {
      <dl>
        <dt>Licencias activas</dt>
        <dd>{{ data.activeLicenses }}</dd>
        <dt>Pendientes vigentes</dt>
        <dd>{{ data.pendingLicenses }}</dd>
        <dt>Activas que vencen en 7 días</dt>
        <dd>{{ data.expiringWithinSevenDays }}</dd>
        <dt>Clientes</dt>
        <dd>{{ data.customers }}</dd>
      </dl>
      <p>Evaluado en UTC: {{ data.evaluatedAt }}</p>
    }
    <button mat-stroked-button [disabled]="busy()" (click)="exportCsv()">
      Descargar licencias CSV
    </button>
    <p>Hasta 10 000 licencias de esta aplicación; incluye datos de clientes y excluye claves.</p>
    <h4>Auditoría de esta aplicación</h4>
    <mat-form-field
      ><mat-label>Acción exacta (opcional)</mat-label
      ><input matInput [formControl]="action" maxlength="100"
    /></mat-form-field>
    <button mat-button [disabled]="busy()" (click)="loadAudit()">Buscar / primera página</button>
    @for (row of audits(); track row.id) {
      <article>
        <strong>{{ row.action }}</strong>
        <p>UTC: {{ row.createdAt }} · Responsable: {{ row.actorId }}</p>
        <p>Recurso: {{ row.entityId || 'Sin recurso' }} · Traza: {{ row.traceId }}</p>
        @if (row.reason) {
          <p>Motivo: {{ row.reason }}</p>
        }
      </article>
    } @empty {
      <p>No hay registros cargados.</p>
    }
    @if (cursor()) {
      <button mat-button [disabled]="busy()" (click)="loadAudit(cursor())">Página siguiente</button>
    }
    <p role="status">{{ message() }}</p>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    dl {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 12px;
    }
    dd {
      margin: 0;
    }
    article {
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      padding: 12px 0;
      overflow-wrap: anywhere;
    }
    mat-form-field {
      width: 100%;
    }
  `,
})
export class AdminReports {
  readonly applicationId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly audits = signal<Audit[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly action = new FormControl('', { nonNullable: true });
  private auditAction = '';
  protected loadDashboard() {
    this.busy.set(true);
    this.http
      .get<Dashboard>('/api/v1/admin/dashboard', {
        params: { applicationId: this.applicationId() },
      })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (value) => {
          this.dashboard.set(value);
          this.message.set('Estadísticas actualizadas.');
        },
        error: (error) => this.failed(error),
      });
  }
  protected loadAudit(cursor: string | null = null) {
    if (!cursor) this.auditAction = this.action.value.trim();
    this.busy.set(true);
    this.http
      .get<{ items: Audit[]; nextCursor: string | null }>('/api/v1/admin/audit-logs', {
        params: {
          applicationId: this.applicationId(),
          pageSize: '25',
          ...(this.auditAction ? { action: this.auditAction } : {}),
          ...(cursor ? { cursor } : {}),
        },
      })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (value) => {
          this.audits.set(value.items);
          this.cursor.set(value.nextCursor);
          this.message.set('Auditoría actualizada.');
        },
        error: (error) => this.failed(error),
      });
  }
  protected exportCsv() {
    this.busy.set(true);
    this.http
      .get('/api/v1/admin/licenses/export', {
        params: { applicationId: this.applicationId() },
        responseType: 'blob',
      })
      .pipe(
        timeout(30000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = 'licenses.csv';
          link.click();
          setTimeout(() => URL.revokeObjectURL(url), 1000);
          this.message.set('CSV descargado.');
        },
        error: (error) => this.failed(error),
      });
  }
  private failed(error: HttpErrorResponse) {
    this.message.set(
      error.status === 409
        ? 'La exportación supera 10 000 filas.'
        : 'No se pudo completar. Comprueba tus permisos y reintenta.',
    );
  }
}
