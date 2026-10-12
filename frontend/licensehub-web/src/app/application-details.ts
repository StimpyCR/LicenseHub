import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { finalize, switchMap, timeout } from 'rxjs';

interface Credential {
  id: string;
  scopes: string[];
  expiresAt: string;
  revokedAt: string | null;
  etag: string;
}
interface Feature {
  code: string;
  name: string;
  isActive: boolean;
  etag: string;
}
interface Page<T> {
  items: T[];
  nextCursor: string | null;
}

@Component({
  selector: 'app-application-details',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h3>Credenciales de integración</h3>
    <p>
      Instala los secretos únicamente en el backend consumidor. Para rotar: emite otra credencial,
      compruébala y revoca la anterior.
    </p>
    <button mat-button (click)="loadCredentials()" [disabled]="busy()">
      Actualizar credenciales
    </button>
    @for (credential of credentials(); track credential.id) {
      <p>
        {{ credential.id }} · {{ credential.scopes.join(', ') }} · Vence {{ credential.expiresAt }}
      </p>
      @if (credential.revokedAt) {
        <p>Revocada {{ credential.revokedAt }}</p>
      } @else {
        <button
          mat-stroked-button
          (click)="revoke(credential)"
          [disabled]="busy() || reason.invalid"
        >
          Revocar
        </button>
      }
    }
    @if (credentialCursor()) {
      <button mat-button [disabled]="busy()" (click)="loadCredentials(credentialCursor())">
        Más credenciales
      </button>
    }
    <mat-form-field
      ><mat-label>Motivo de revocación</mat-label
      ><input matInput [formControl]="reason" maxlength="1000"
    /></mat-form-field>
    <form [formGroup]="credentialForm" (ngSubmit)="issue()">
      <mat-form-field
        ><mat-label>Duración de credencial (horas)</mat-label
        ><input matInput type="number" formControlName="hours" min="1" max="8760"
      /></mat-form-field>
      <label><input type="checkbox" formControlName="read" /> Consultar licencias</label>
      <label><input type="checkbox" formControlName="activate" /> Activar licencias</label>
      <label><input type="checkbox" formControlName="warnings" /> Consultar avisos</label>
      <button mat-flat-button [disabled]="busy() || credentialForm.invalid" type="submit">
        Emitir credencial
      </button>
    </form>
    @if (secret()) {
      <p>
        Secreto mostrado solo ahora. Guárdalo en el gestor de secretos del backend antes de cerrar.
      </p>
      <mat-form-field
        ><mat-label>Secreto emitido</mat-label
        ><textarea matInput readonly [value]="secret()"></textarea>
      </mat-form-field>
      <button mat-button (click)="secret.set('')">Ocultar secreto</button>
    }
    <h3>Funcionalidades Premium</h3>
    <button mat-button [disabled]="busy()" (click)="loadFeatures()">Actualizar catálogo</button>
    @for (feature of features(); track feature.code) {
      <p>
        {{ feature.code }} · {{ feature.name }} · {{ feature.isActive ? 'Activa' : 'Inactiva' }}
      </p>
      <button mat-stroked-button [disabled]="busy()" (click)="editFeature(feature)">
        Editar funcionalidad
      </button>
    }
    @if (featureCursor()) {
      <button mat-button [disabled]="busy()" (click)="loadFeatures(featureCursor())">
        Más funcionalidades
      </button>
    }
    <form [formGroup]="featureForm" (ngSubmit)="saveFeature()">
      <mat-form-field
        ><mat-label>Código inmutable</mat-label
        ><input matInput formControlName="code" maxlength="64"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Nombre</mat-label><input matInput formControlName="name" maxlength="120"
      /></mat-form-field>
      <label><input type="checkbox" formControlName="isActive" /> Funcionalidad activa</label>
      <button mat-flat-button type="submit" [disabled]="busy() || featureForm.invalid">
        Guardar funcionalidad
      </button>
      <button mat-button type="button" [disabled]="busy()" (click)="newFeature()">
        Nueva funcionalidad
      </button>
    </form>
    @if (message()) {
      <p role="status">{{ message() }}</p>
    }
  `,
  styles: `
    form {
      display: grid;
      gap: 16px;
      margin-block: 16px;
    }
    mat-form-field {
      width: 100%;
      min-width: 0;
    }
    p {
      overflow-wrap: anywhere;
    }
  `,
})
export class ApplicationDetails {
  readonly applicationId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly credentials = signal<Credential[]>([]);
  protected readonly features = signal<Feature[]>([]);
  protected readonly credentialCursor = signal<string | null>(null);
  protected readonly featureCursor = signal<string | null>(null);
  protected readonly secret = signal('');
  protected readonly message = signal('');
  protected readonly busy = signal(false);
  private selectedFeature: Feature | null = null;
  private attempt: { fingerprint: string; key: string; body: object } | null = null;
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(1000)],
  });
  protected readonly credentialForm = new FormGroup({
    hours: new FormControl(720, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(8760)],
    }),
    read: new FormControl(true, { nonNullable: true }),
    activate: new FormControl(false, { nonNullable: true }),
    warnings: new FormControl(false, { nonNullable: true }),
  });
  protected readonly featureForm = new FormGroup({
    code: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[a-z][a-z0-9._-]{0,63}$/)],
    }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    effect(() => {
      this.applicationId();
      this.secret.set('');
      this.credentials.set([]);
      this.features.set([]);
      this.credentialCursor.set(null);
      this.featureCursor.set(null);
      this.message.set('');
      this.attempt = null;
      this.newFeature();
      this.loadCredentials();
      this.loadFeatures();
    });
  }
  private base() {
    return '/api/v1/admin/applications/' + this.applicationId();
  }
  protected loadCredentials(cursor: string | null = null) {
    const app = this.applicationId();
    this.http
      .get<Page<Credential>>(this.base() + '/credentials', { params: cursor ? { cursor } : {} })
      .pipe(timeout(10000), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          if (app === this.applicationId()) {
            this.credentials.set(page.items);
            this.credentialCursor.set(page.nextCursor);
          }
        },
        error: () =>
          this.message.set(
            'No se pudieron consultar credenciales. Comprueba tus permisos y sesión.',
          ),
      });
  }
  protected loadFeatures(cursor: string | null = null) {
    const app = this.applicationId();
    this.http
      .get<Page<Feature>>(this.base() + '/features', { params: cursor ? { cursor } : {} })
      .pipe(timeout(10000), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          if (app === this.applicationId()) {
            this.features.set(page.items);
            this.featureCursor.set(page.nextCursor);
          }
        },
        error: () =>
          this.message.set('No se pudo consultar el catálogo. Comprueba tus permisos y sesión.'),
      });
  }
  protected issue() {
    if (this.credentialForm.invalid || this.busy()) return;
    const values = this.credentialForm.getRawValue();
    const scopes = [
      values.read ? 'licenses:read' : '',
      values.activate ? 'licenses:activate' : '',
      values.warnings ? 'warnings:read' : '',
    ].filter(Boolean);
    if (!scopes.length) {
      this.message.set('Selecciona al menos un permiso.');
      return;
    }
    const fingerprint = 'issue:' + this.applicationId() + JSON.stringify(values);
    if (this.secret()) {
      this.message.set('Guarda y oculta el secreto actual antes de emitir otro.');
      return;
    }
    this.mutate<{ secret: string }>(
      'POST',
      this.base() + '/credentials',
      fingerprint,
      { scopes, expiresAt: new Date(Date.now() + values.hours * 3600000).toISOString() },
      null,
      (result) => {
        this.secret.set(result.secret);
        this.loadCredentials();
      },
    );
  }
  protected revoke(credential: Credential) {
    if (this.reason.invalid || this.busy()) return;
    const body = { reason: this.reason.value };
    this.mutate(
      'POST',
      '/api/v1/admin/credentials/' + credential.id + '/revoke',
      'revoke:' + credential.id + credential.etag + JSON.stringify(body),
      body,
      credential.etag,
      () => this.loadCredentials(),
    );
  }
  protected editFeature(feature: Feature) {
    this.selectedFeature = feature;
    this.featureForm.setValue({
      code: feature.code,
      name: feature.name,
      isActive: feature.isActive,
    });
    this.featureForm.controls.code.disable();
  }
  protected newFeature() {
    this.selectedFeature = null;
    this.featureForm.controls.code.enable();
    this.featureForm.reset({ code: '', name: '', isActive: true });
  }
  protected saveFeature() {
    if (this.featureForm.invalid || this.busy()) return;
    const body = this.featureForm.getRawValue();
    const feature = this.selectedFeature;
    this.mutate(
      feature ? 'PUT' : 'POST',
      this.base() + '/features' + (feature ? '/' + encodeURIComponent(feature.code) : ''),
      'feature:' + this.applicationId() + JSON.stringify(body) + feature?.etag,
      body,
      feature?.etag ?? null,
      () => {
        this.newFeature();
        this.loadFeatures();
      },
    );
  }
  private mutate<T>(
    method: string,
    url: string,
    fingerprint: string,
    body: object,
    etag: string | null,
    success: (result: T) => void,
  ) {
    if (this.attempt?.fingerprint !== fingerprint)
      this.attempt = { fingerprint, key: crypto.randomUUID(), body };
    const attempt = this.attempt;
    const app = this.applicationId();
    this.busy.set(true);
    this.message.set('');
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap(({ token }) =>
          this.http.request<T>(method, url, {
            body: attempt.body,
            headers: {
              'X-CSRF-TOKEN': token,
              'Idempotency-Key': attempt.key,
              ...(etag ? { 'If-Match': etag } : {}),
            },
          }),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          if (app === this.applicationId()) {
            this.attempt = null;
            success(result);
            this.message.set('Operación completada.');
          }
        },
        error: (error: HttpErrorResponse) => {
          if (error.error?.code === 'secret_not_recoverable') {
            this.attempt = null;
            this.loadCredentials();
            this.message.set(
              'La credencial se emitió, pero su secreto no se puede recuperar. Consulta el listado, revócala y emite otra.',
            );
          } else
            this.message.set(
              error.status === 412
                ? 'El recurso cambió. Actualiza el listado antes de editar.'
                : 'No se pudo completar. Reintenta los mismos datos o comprueba tus permisos y sesión.',
            );
        },
      });
  }
}
