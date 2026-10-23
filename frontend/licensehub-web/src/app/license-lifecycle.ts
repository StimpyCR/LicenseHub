import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { finalize, forkJoin, switchMap, timeout } from 'rxjs';

export interface ManagedLicense {
  id: string;
  etag: string;
  state: string;
  isLifetime: boolean;
  expiresAt: string | null;
  activatedAt: string | null;
}
interface Renewal {
  id: string;
  beforeExpiresAt: string | null;
  afterExpiresAt: string | null;
  afterLifetime: boolean;
  reason: string;
  createdAt: string;
}
interface Activation {
  id: string;
  subjectId: string;
  activatedAt: string;
}

@Component({
  selector: 'app-license-lifecycle',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
  ],
  template: `
    @if (current(); as item) {
      <h4>Administrar licencia</h4>
      <p class="identifier">{{ item.id }} · {{ item.state }}</p>
      <p>
        {{
          item.isLifetime
            ? 'Vitalicia'
            : 'Vence: ' + ((item.expiresAt | date: 'medium') || 'Pendiente de activación')
        }}
      </p>
      <p>
        Suspensión y revocación invalidan derechos en el servidor. Una caché autorizada puede
        conservarlos hasta 30 segundos.
      </p>
      <form [formGroup]="form" (ngSubmit)="renew()">
        <mat-form-field
          ><mat-label>Motivo administrativo</mat-label
          ><textarea matInput formControlName="reason" maxlength="1000"></textarea>
        </mat-form-field>
        <mat-form-field
          ><mat-label>Renovar por</mat-label
          ><select matNativeControl formControlName="kind">
            <option value="Duration">Duración</option>
            <option value="Until">Fecha específica</option>
            <option value="Lifetime">Vitalicia</option>
          </select></mat-form-field
        >
        @if (form.controls.kind.value === 'Duration') {
          <mat-form-field
            ><mat-label>Valor de renovación</mat-label
            ><input matInput type="number" formControlName="value" min="1" max="1000000"
          /></mat-form-field>
          <mat-form-field
            ><mat-label>Unidad de renovación</mat-label
            ><select matNativeControl formControlName="unit">
              <option value="Minute">Minutos</option>
              <option value="Hour">Horas</option>
              <option value="Day">Días</option>
              <option value="Month">Meses calendario</option>
              <option value="Year">Años calendario</option>
            </select></mat-form-field
          >
        }
        @if (form.controls.kind.value === 'Until') {
          <mat-form-field
            ><mat-label>Nuevo vencimiento local</mat-label
            ><input matInput type="datetime-local" formControlName="until" /><mat-hint
              >Debe superar ahora y el vencimiento actual.</mat-hint
            ></mat-form-field
          >
        }
        <mat-checkbox formControlName="reactivate">Autorizar reactivación al renovar</mat-checkbox>
        <p>
          Solo licencias canjeadas. Suspendidas/revocadas exigen esta autorización y el permiso
          correspondiente.
        </p>
        <button
          mat-flat-button
          type="submit"
          [disabled]="busy() || form.invalid || !item.activatedAt || item.state === 'Cancelled'"
        >
          Renovar licencia
        </button>
        <button mat-button type="button" [disabled]="busy()" (click)="prepareRenewal()">
          Preparar otra renovación
        </button>
        <div class="actions">
          <button
            mat-stroked-button
            type="button"
            [disabled]="busy() || reasonInvalid()"
            (click)="change('suspend')"
          >
            Suspender licencia
          </button>
          <button
            mat-stroked-button
            type="button"
            [disabled]="busy() || reasonInvalid()"
            (click)="change('revoke')"
          >
            Revocar licencia
          </button>
          <button
            mat-stroked-button
            type="button"
            [disabled]="busy() || reasonInvalid()"
            (click)="change('reactivate')"
          >
            Reactivar suspensión
          </button>
          <button
            mat-stroked-button
            type="button"
            [disabled]="busy() || reasonInvalid()"
            (click)="change('cancel')"
          >
            Cancelar definitivamente
          </button>
        </div>
      </form>
      <button mat-button [disabled]="busy()" (click)="history()">Consultar historial</button>
      @for (activation of activations(); track activation.id) {
        <p>
          Activación: {{ activation.subjectId }} · {{ activation.activatedAt | date: 'medium' }}
        </p>
      }
      @for (renewal of renewals(); track renewal.id) {
        <p>
          {{ renewal.createdAt | date: 'medium' }} · {{ renewal.reason }} ·
          {{ renewal.afterLifetime ? 'Vitalicia' : (renewal.afterExpiresAt | date: 'medium') }}
        </p>
      }
      @if (historyCursor()) {
        <button mat-button [disabled]="busy()" (click)="history(historyCursor())">
          Renovaciones siguientes
        </button>
      }
    }
    @if (message()) {
      <p role="status">{{ message() }}</p>
    }
  `,
  styles: `
    form {
      display: grid;
      gap: 12px;
      margin: 16px 0;
    }
    mat-form-field {
      width: 100%;
      min-width: 0;
    }
    p {
      overflow-wrap: anywhere;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    button {
      white-space: normal;
    }
  `,
})
export class LicenseLifecycle {
  isWorking(): boolean {
    return this.busy();
  }
  hasPendingWork(): boolean {
    return this.busy() || this.form.dirty;
  }

  readonly license = input.required<ManagedLicense>();
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly current = signal<ManagedLicense | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly activations = signal<Activation[]>([]);
  protected readonly renewals = signal<Renewal[]>([]);
  protected readonly historyCursor = signal<string | null>(null);
  protected readonly form = new FormGroup({
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(1000)],
    }),
    kind: new FormControl('Duration', { nonNullable: true }),
    unit: new FormControl('Day', { nonNullable: true }),
    value: new FormControl(30, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(1000000)],
    }),
    until: new FormControl('', { nonNullable: true }),
    reactivate: new FormControl(false, { nonNullable: true }),
  });
  private intent: { fingerprint: string; key: string; operationId: string; etag: string } | null =
    null;
  constructor() {
    effect(() => {
      this.current.set(this.license());
      this.intent = null;
      this.message.set('');
      this.activations.set([]);
      this.renewals.set([]);
      this.historyCursor.set(null);
    });
  }
  protected reasonInvalid() {
    return this.form.controls.reason.invalid || !this.form.controls.reason.value.trim();
  }
  protected prepareRenewal() {
    this.intent = null;
    this.message.set('Nueva renovación preparada. El siguiente envío aplicará otra extensión.');
  }
  protected renew() {
    const item = this.current();
    if (!item || this.busy() || this.form.invalid || this.reasonInvalid()) return;
    const value = this.form.getRawValue();
    if (value.kind === 'Until' && (!value.until || Number.isNaN(new Date(value.until).getTime()))) {
      this.message.set('Introduce un vencimiento válido.');
      return;
    }
    const term =
      value.kind === 'Lifetime'
        ? { kind: 'Lifetime' }
        : value.kind === 'Until'
          ? { kind: 'Until', until: new Date(value.until).toISOString() }
          : { kind: 'Duration', duration: { unit: value.unit, value: value.value } };
    const body = { term, reactivate: value.reactivate, reason: value.reason };
    this.mutate('renew', body, true);
  }
  protected change(command: string) {
    if (this.busy() || this.reasonInvalid()) return;
    this.mutate(command, { reason: this.form.controls.reason.value }, false);
  }
  private mutate(command: string, body: object, renewal: boolean) {
    const item = this.current();
    if (!item) return;
    const fingerprint = JSON.stringify({ id: item.id, command, body });
    if (this.intent?.fingerprint !== fingerprint)
      this.intent = {
        fingerprint,
        key: crypto.randomUUID(),
        operationId: crypto.randomUUID(),
        etag: item.etag,
      };
    const intent = this.intent;
    // Capture the original ETag for retries even after a successful response changes the current view.
    const etag = intent.etag;
    this.busy.set(true);
    this.message.set('');
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap((csrf) =>
          this.http.post<ManagedLicense>(
            '/api/v1/admin/licenses/' + item.id + '/' + command,
            renewal ? { ...body, operationId: intent.operationId } : body,
            {
              headers: {
                'Idempotency-Key': intent.key,
                'If-Match': etag,
                'X-CSRF-TOKEN': csrf.token,
              },
            },
          ),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (value) => {
          this.current.set(value);
          this.message.set(
            'Operación registrada. Actualiza el listado para consultar sus nuevos datos.',
          );
        },
        error: (e) => this.failure(e),
      });
  }
  protected history(cursor: string | null = null) {
    const item = this.current();
    if (!item || this.busy()) return;
    this.busy.set(true);
    forkJoin({
      activations: this.http.get<{ items: Activation[] }>(
        '/api/v1/admin/licenses/' + item.id + '/activations',
      ),
      renewals: this.http.get<{ items: Renewal[]; nextCursor: string | null }>(
        '/api/v1/admin/licenses/' + item.id + '/renewals',
        { params: { pageSize: 25, ...(cursor ? { cursor } : {}) } },
      ),
    })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.activations.set(result.activations.items);
          this.renewals.set(result.renewals.items);
          this.historyCursor.set(result.renewals.nextCursor);
        },
        error: (e) => this.failure(e),
      });
  }
  private failure(error: unknown) {
    const code = error instanceof HttpErrorResponse ? error.error?.code : '';
    const messages: Record<string, string> = {
      invalid_transition:
        'El estado actual no permite esta operación. Revisa si requiere renovación o reactivación.',
      renewal_already_applied: 'Esta renovación ya se aplicó; no se extendió otra vez.',
      subject_has_license: 'El negocio tiene otra licencia activa o suspendida.',
      precondition_failed: 'La licencia cambió. Actualiza el listado y vuelve a seleccionarla.',
      not_found: 'Recurso inexistente o sin permiso para esta aplicación.',
      validation_failed: 'Revisa motivo, duración y vencimiento.',
      idempotency_conflict: 'Los datos del reintento cambiaron.',
    };
    this.message.set(
      messages[code] ??
        'No se pudo confirmar la operación. Reintenta los mismos datos para comprobar el resultado.',
    );
  }
}
