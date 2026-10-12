import { Component, DestroyRef, inject, input, signal, viewChild } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  FormGroupDirective,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { finalize, switchMap, timeout } from 'rxjs';
import { ApplicationDetails } from './application-details';
import { LicenseManager } from './license-manager';

interface ApplicationView {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  etag: string;
}

@Component({
  selector: 'app-application-manager',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    ApplicationDetails,
    LicenseManager,
  ],
  template: `
    <h3>Aplicaciones</h3>
    <button mat-button [disabled]="busy()" (click)="load()">Actualizar lista</button>
    @for (app of applications(); track app.id) {
      <p>{{ app.name }} · {{ app.slug }} · {{ app.isActive ? 'Activa' : 'Inactiva' }}</p>
      <button mat-button [disabled]="busy()" (click)="details.set(app)">
        Credenciales y funcionalidades
      </button>
      <button mat-button [disabled]="busy()" (click)="licensing.set(app)">
        Clientes y licencias
      </button>
      @if (canManage()) {
        <button mat-stroked-button [disabled]="busy()" (click)="edit(app)">Editar</button>
      }
    } @empty {
      <p>No hay aplicaciones en esta página.</p>
    }
    @if (cursor()) {
      <button mat-button [disabled]="busy()" (click)="load(cursor())">Página siguiente</button>
    }
    @if (canManage()) {
      <h3>{{ selected() ? 'Editar aplicación' : 'Registrar aplicación' }}</h3>
      <form #applicationFormDirective="ngForm" [formGroup]="form" (ngSubmit)="save()">
        <mat-form-field
          ><mat-label>Nombre</mat-label><input matInput formControlName="name" maxlength="120"
        /></mat-form-field>
        <mat-form-field
          ><mat-label>Identificador (slug)</mat-label
          ><input matInput formControlName="slug" maxlength="64" />
          <mat-hint>Minúsculas, números y guiones; no cambia después de crear.</mat-hint>
        </mat-form-field>
        <mat-checkbox formControlName="isActive">Aplicación activa</mat-checkbox>
        <p>Desactivarla impide autenticar sus integraciones.</p>
        <button mat-flat-button type="submit" [disabled]="busy() || form.invalid">Guardar</button>
        <button mat-button type="button" [disabled]="busy()" (click)="reset()">
          Nueva aplicación
        </button>
      </form>
    }
    @if (message()) {
      <p role="status">{{ message() }}</p>
    }
    @if (details(); as app) {
      <h3>{{ app.name }}</h3>
      <button mat-button (click)="details.set(null)">Cerrar detalles</button>
      @defer (on immediate) {
        <app-application-details [applicationId]="app.id" />
      }
    }
    @for (app of licensing() ? [licensing()!] : []; track app.id) {
      <h3>Clientes y licencias · {{ app.name }}</h3>
      <button mat-button (click)="licensing.set(null)">Cerrar clientes y licencias</button>
      @defer (on immediate) {
        <app-license-manager [applicationId]="app.id" />
      }
    }
  `,
  styles: `
    form {
      display: grid;
      gap: 16px;
      margin-top: 16px;
    }
    mat-form-field {
      width: 100%;
      min-width: 0;
    }
  `,
})
export class ApplicationManager {
  protected readonly licensing = signal<ApplicationView | null>(null);
  readonly canManage = input(false);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly applicationFormDirective = viewChild<FormGroupDirective>(
    'applicationFormDirective',
  );
  protected readonly applications = signal<ApplicationView[]>([]);
  protected readonly details = signal<ApplicationView | null>(null);
  protected readonly cursor = signal<string | null>(null);
  protected readonly selected = signal<ApplicationView | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  private attempt: { fingerprint: string; key: string } | null = null;
  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    slug: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[a-z][a-z0-9-]{0,63}$/)],
    }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    this.load();
  }
  protected load(cursor: string | null = null) {
    this.busy.set(true);
    this.http
      .get<{ items: ApplicationView[]; nextCursor: string | null }>('/api/v1/admin/applications', {
        params: cursor ? { cursor } : {},
      })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (page) => {
          this.applications.set(page.items);
          this.cursor.set(page.nextCursor);
        },
        error: () => this.message.set('No se pudo consultar la lista. Comprueba tu sesión.'),
      });
  }
  protected edit(app: ApplicationView) {
    this.selected.set(app);
    this.form.setValue({ name: app.name, slug: app.slug, isActive: app.isActive });
    this.form.controls.slug.disable();
    this.attempt = null;
  }
  protected reset() {
    this.selected.set(null);
    this.form.controls.slug.enable();
    const values = { name: '', slug: '', isActive: true };
    if (this.applicationFormDirective()) this.applicationFormDirective()!.resetForm(values);
    else this.form.reset(values);
    this.attempt = null;
  }
  protected save() {
    if (this.form.invalid || this.busy()) return;
    const body = this.form.getRawValue();
    const app = this.selected();
    const fingerprint = JSON.stringify({ body, id: app?.id, etag: app?.etag });
    if (this.attempt?.fingerprint !== fingerprint)
      this.attempt = { fingerprint, key: crypto.randomUUID() };
    const key = this.attempt!.key;
    this.busy.set(true);
    this.message.set('');
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap(({ token }) =>
          this.http.request<ApplicationView>(
            app ? 'PUT' : 'POST',
            '/api/v1/admin/applications' + (app ? '/' + app.id : ''),
            {
              body,
              headers: {
                'X-CSRF-TOKEN': token,
                'Idempotency-Key': key,
                ...(app ? { 'If-Match': app.etag } : {}),
              },
            },
          ),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.reset();
          this.message.set('Aplicación guardada.');
          this.load();
        },
        error: (error: HttpErrorResponse) =>
          this.message.set(
            error.status === 412
              ? 'La aplicación cambió. Actualiza la lista y vuelve a editar.'
              : 'No se pudo guardar. Reintenta los mismos datos o comprueba tus permisos.',
          ),
      });
  }
}
