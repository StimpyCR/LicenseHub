import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { finalize, switchMap, timeout } from 'rxjs';

interface Payment {
  id: string;
  licenseId: string;
  customerId: string;
  amount: string;
  currency: string;
  reference: string;
  state: string;
  etag: string;
  confirmedAt: string | null;
  renewalIntent: {
    operationId: string;
    term: { kind: string; duration?: { unit: string; value: number } };
    reactivate: boolean;
    reason: string;
  } | null;
}

@Component({
  selector: 'app-payment-manager',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
  ],
  template: `
    <h4>Pagos manuales</h4>
    <p>
      Este panel registra pagos y reembolsos realizados fuera de LicenseHub. No cobra ni mueve
      dinero.
    </p>
    <button mat-button [disabled]="busy()" (click)="loadLicenses()">
      Consultar licencias para el pago
    </button>
    @for (license of licenseOptions(); track license.id) {
      <p class="identifier">{{ license.id }} · {{ license.state }}</p>
      <button
        mat-button
        [disabled]="busy() || !license.customerId"
        (click)="chooseLicense(license)"
      >
        Seleccionar licencia para pago
      </button>
    }
    @if (licenseCursor()) {
      <button mat-button [disabled]="busy()" (click)="loadLicenses(licenseCursor())">
        Licencias siguientes para pago
      </button>
    }
    <form [formGroup]="form" (ngSubmit)="create()">
      <mat-form-field
        ><mat-label>Licencia (UUID)</mat-label><input matInput formControlName="licenseId"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Cliente de la licencia (UUID)</mat-label
        ><input matInput formControlName="customerId"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Importe exacto, cuatro decimales</mat-label
        ><input matInput formControlName="amount" inputmode="decimal" placeholder="10.0000"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Moneda</mat-label><input matInput formControlName="currency" maxlength="3"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Referencia única del comprobante</mat-label
        ><input matInput formControlName="reference" maxlength="120"
      /></mat-form-field>
      <mat-checkbox formControlName="renew">Renovar al confirmar este pago</mat-checkbox>
      @if (form.controls.renew.value) {
        <mat-form-field
          ><mat-label>Días de renovación</mat-label
          ><input matInput formControlName="days" type="number" min="1" max="1000000"
        /></mat-form-field>
        <mat-checkbox formControlName="reactivate">Solicitar reactivación autorizada</mat-checkbox>
        <mat-form-field
          ><mat-label>Motivo de la renovación</mat-label
          ><textarea matInput formControlName="renewalReason" maxlength="1000"></textarea>
        </mat-form-field>
      }
      <button mat-flat-button type="submit" [disabled]="busy() || form.invalid">
        Registrar pago pendiente
      </button>
    </form>
    <button mat-button [disabled]="busy()" (click)="load()">Consultar pagos</button>
    @for (payment of payments(); track payment.id) {
      <p class="identifier">
        {{ payment.id }} · {{ payment.amount }} {{ payment.currency }} · {{ payment.state }}
      </p>
      <p>Comprobante: {{ payment.reference }}</p>
      @if (payment.renewalIntent; as intent) {
        <p>
          Al confirmar: {{ intent.term.duration?.value }} {{ intent.term.duration?.unit }} ·
          {{ intent.reason }} · Reactivación: {{ intent.reactivate ? 'Sí' : 'No' }}
        </p>
      }
      @if (payment.confirmedAt) {
        <p>Confirmado: {{ payment.confirmedAt | date: 'medium' }}</p>
      }
      <button mat-button [disabled]="busy()" (click)="selected.set(payment)">
        Revisar este pago
      </button>
    } @empty {
      <p>No hay pagos cargados.</p>
    }
    @if (cursor()) {
      <button mat-button [disabled]="busy()" (click)="load(cursor())">Pagos siguientes</button>
    }
    @if (selected(); as payment) {
      <h4>Revisión del comprobante</h4>
      <p class="identifier">
        {{ payment.reference }} · {{ payment.amount }} {{ payment.currency }} · {{ payment.state }}
      </p>
      @if (payment.renewalIntent; as intent) {
        <p>
          Confirmar aplicará esta intención: {{ intent.term.kind }}
          {{ intent.term.duration?.value }} {{ intent.term.duration?.unit }}. Motivo:
          {{ intent.reason }}. Reactivar: {{ intent.reactivate ? 'Sí' : 'No' }}.
        </p>
      }
      <p>
        Revisa el comprobante externo y la intención antes de confirmar. Registrar un reembolso
        conserva los derechos; para retirarlos usa la administración de la licencia.
      </p>
      <mat-form-field
        ><mat-label>Motivo de revisión del pago</mat-label
        ><textarea matInput [formControl]="reason" maxlength="1000"></textarea>
      </mat-form-field>
      <button mat-stroked-button [disabled]="busy() || reason.invalid" (click)="change('confirm')">
        Confirmar pago registrado
      </button>
      <button mat-stroked-button [disabled]="busy() || reason.invalid" (click)="change('reject')">
        Rechazar comprobante
      </button>
      <button mat-stroked-button [disabled]="busy() || reason.invalid" (click)="change('refund')">
        Registrar reembolso manual
      </button>
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
    button {
      white-space: normal;
      margin: 4px 0;
    }
  `,
})
export class PaymentManager {
  protected readonly licenseOptions = signal<
    { id: string; customerId: string | null; state: string }[]
  >([]);
  protected readonly licenseCursor = signal<string | null>(null);
  readonly applicationId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly payments = signal<Payment[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly selected = signal<Payment | null>(null);
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(1000)],
  });
  protected readonly form = new FormGroup({
    licenseId: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[0-9a-f-]{36}$/i)],
    }),
    customerId: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[0-9a-f-]{36}$/i)],
    }),
    amount: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[0-9]{1,15}\.[0-9]{4}$/)],
    }),
    currency: new FormControl('USD', {
      nonNullable: true,
      validators: Validators.pattern(/^[A-Z]{3}$/),
    }),
    reference: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    renew: new FormControl(false, { nonNullable: true }),
    days: new FormControl(30, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(1000000)],
    }),
    reactivate: new FormControl(false, { nonNullable: true }),
    renewalReason: new FormControl('', { nonNullable: true }),
  });
  private createIntent: { body: string; key: string; operationId: string } | null = null;
  private changeIntent: { body: string; key: string; etag: string } | null = null;
  protected loadLicenses(cursor: string | null = null) {
    if (this.busy()) return;
    this.busy.set(true);
    this.http
      .get<{
        items: { id: string; customerId: string | null; state: string }[];
        nextCursor: string | null;
      }>('/api/v1/admin/licenses', {
        params: {
          applicationId: this.applicationId(),
          pageSize: 25,
          ...(cursor ? { cursor } : {}),
        },
      })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (p) => {
          this.licenseOptions.set(p.items);
          this.licenseCursor.set(p.nextCursor);
        },
        error: (e) => this.failure(e),
      });
  }
  protected chooseLicense(license: { id: string; customerId: string | null }) {
    if (!license.customerId) return;
    this.form.controls.licenseId.setValue(license.id);
    this.form.controls.customerId.setValue(license.customerId);
  }
  protected load(cursor: string | null = null) {
    if (this.busy()) return;
    this.busy.set(true);
    this.http
      .get<{ items: Payment[]; nextCursor: string | null }>('/api/v1/admin/payments', {
        params: {
          applicationId: this.applicationId(),
          pageSize: 25,
          ...(cursor ? { cursor } : {}),
        },
      })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (p) => {
          this.payments.set(p.items);
          this.cursor.set(p.nextCursor);
        },
        error: (e) => this.failure(e),
      });
  }
  protected create() {
    if (this.busy() || this.form.invalid) return;
    const value = this.form.getRawValue();
    if (value.renew && !value.renewalReason.trim()) {
      this.message.set('Introduce el motivo de renovación.');
      return;
    }
    const fingerprint = JSON.stringify({ applicationId: this.applicationId(), value });
    if (this.createIntent?.body !== fingerprint)
      this.createIntent = {
        body: fingerprint,
        key: crypto.randomUUID(),
        operationId: crypto.randomUUID(),
      };
    const body = {
      applicationId: this.applicationId(),
      licenseId: value.licenseId,
      customerId: value.customerId,
      amount: value.amount,
      currency: value.currency,
      reference: value.reference,
      renewalIntent: value.renew
        ? {
            operationId: this.createIntent.operationId,
            term: { kind: 'Duration', duration: { unit: 'Day', value: value.days } },
            reactivate: value.reactivate,
            reason: value.renewalReason,
          }
        : null,
    };
    this.mutate('/api/v1/admin/payments', body, { 'Idempotency-Key': this.createIntent.key });
  }
  protected change(command: string) {
    const selected = this.selected();
    if (!selected || this.busy() || this.reason.invalid) return;
    const body = { reason: this.reason.value };
    const fingerprint = JSON.stringify({ id: selected.id, command, body });
    if (this.changeIntent?.body !== fingerprint)
      this.changeIntent = { body: fingerprint, key: crypto.randomUUID(), etag: selected.etag };
    this.mutate('/api/v1/admin/payments/' + selected.id + '/' + command, body, {
      'Idempotency-Key': this.changeIntent.key,
      'If-Match': this.changeIntent.etag,
    });
  }
  private mutate(url: string, body: object, headers: Record<string, string>) {
    this.busy.set(true);
    this.message.set('');
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap((csrf) =>
          this.http.post<Payment>(url, body, {
            headers: { ...headers, 'X-CSRF-TOKEN': csrf.token },
          }),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (value) => {
          this.selected.set(value);
          this.message.set('Registro actualizado. Consulta la lista para ver sus nuevos datos.');
        },
        error: (e) => this.failure(e),
      });
  }
  private failure(error: unknown) {
    const code = error instanceof HttpErrorResponse ? error.error?.code : '';
    const messages: Record<string, string> = {
      conflict: 'La referencia del comprobante ya existe.',
      invalid_transition: 'El estado del pago o licencia no permite esta operación.',
      precondition_failed: 'El registro cambió. Consulta de nuevo antes de editar.',
      not_found: 'Recurso inexistente o sin permiso.',
      renewal_already_applied: 'La intención de renovación ya fue aplicada.',
      validation_failed: 'Revisa UUID, importe exacto, moneda, referencia y motivo.',
    };
    this.message.set(
      messages[code] ?? 'No se pudo confirmar el resultado. Reintenta con los mismos datos.',
    );
  }
}
