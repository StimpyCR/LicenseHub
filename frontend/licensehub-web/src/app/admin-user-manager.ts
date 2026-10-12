import { Component, DestroyRef, inject, signal, viewChild } from '@angular/core';
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
import { finalize, switchMap, timeout } from 'rxjs';

interface Grant {
  applicationId: string;
  capability: string;
}
interface Profile {
  id: string;
  email: string;
  isDisabled: boolean;
  mfaEnrolled: boolean;
  isSuperAdmin: boolean;
  etag: string;
  grants: Grant[];
}
interface Application {
  id: string;
  name: string;
}

@Component({
  selector: 'app-admin-user-manager',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h3>Administradores</h3>
    <button mat-button [disabled]="busy()" (click)="load()">Actualizar usuarios</button>
    @for (user of users(); track user.id) {
      <p>
        {{ user.email }} · {{ user.isSuperAdmin ? 'SuperAdmin' : 'Delegado' }} ·
        {{
          user.isDisabled
            ? 'Suspendido'
            : user.mfaEnrolled
              ? 'MFA enrolado'
              : 'Pendiente de enrolamiento'
        }}
      </p>
      <p>ID: {{ user.id }}</p>
      @if (!user.isSuperAdmin) {
        <button mat-stroked-button [disabled]="busy()" (click)="edit(user)">
          Configurar acceso
        </button>
      }
    }
    @if (cursor()) {
      <button mat-button [disabled]="busy()" (click)="load(cursor())">Más usuarios</button>
    }
    <form #createFormDirective="ngForm" [formGroup]="createForm" (ngSubmit)="create()">
      <mat-form-field
        ><mat-label>Correo del nuevo administrador</mat-label
        ><input matInput formControlName="email" type="email" maxlength="254"
      /></mat-form-field>
      <p>
        Se crea sin contraseña, MFA ni permisos. El titular debe completar el enrolamiento privado
        antes de acceder.
      </p>
      <button mat-flat-button type="submit" [disabled]="busy() || email.invalid">
        Crear cuenta pendiente
      </button>
    </form>
    @if (selected(); as user) {
      <h3>Acceso de {{ user.email }}</h3>
      <label
        ><input type="checkbox" [formControl]="disabled" /> Suspender acceso administrativo</label
      >
      @for (grant of grants(); track grant.applicationId + ':' + grant.capability) {
        <p>{{ applicationName(grant.applicationId) }} · {{ grant.capability }}</p>
        <button mat-button [disabled]="busy()" (click)="remove(grant)">Quitar permiso</button>
      }
      <form [formGroup]="grantForm" (ngSubmit)="add()">
        <label
          >Aplicación<select formControlName="applicationId">
            <option value="">Selecciona aplicación</option>
            @for (app of applications(); track app.id) {
              <option [value]="app.id">{{ app.name }}</option>
            }
          </select></label
        >
        <label
          >Permiso<select formControlName="capability">
            @for (capability of capabilities; track capability) {
              <option [value]="capability">{{ capability }}</option>
            }
          </select></label
        >
        <button mat-stroked-button type="submit" [disabled]="busy() || grantForm.invalid">
          Añadir permiso
        </button>
      </form>
      <button mat-button [disabled]="busy() || !applicationCursor()" (click)="moreApplications()">
        Cargar más aplicaciones
      </button>
      <mat-form-field
        ><mat-label>Motivo del cambio</mat-label
        ><input matInput [formControl]="reason" maxlength="1000"
      /></mat-form-field>
      <p>
        Guardar reemplaza los permisos y cierra las sesiones anteriores de este usuario. El MFA
        configurado se conserva.
      </p>
      <button mat-flat-button [disabled]="busy() || reason.invalid" (click)="save()">
        Guardar acceso
      </button>
      <button mat-button [disabled]="busy()" (click)="selected.set(null)">Cancelar edición</button>
    }
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
    mat-form-field,
    select {
      width: 100%;
      min-width: 0;
    }
    p {
      overflow-wrap: anywhere;
    }
    label {
      display: block;
    }
  `,
})
export class AdminUserManager {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly createFormDirective = viewChild<FormGroupDirective>('createFormDirective');
  protected readonly users = signal<Profile[]>([]);
  protected readonly applications = signal<Application[]>([]);
  protected readonly applicationCursor = signal<string | null>(null);
  protected readonly cursor = signal<string | null>(null);
  protected readonly selected = signal<Profile | null>(null);
  protected readonly grants = signal<Grant[]>([]);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly capabilities = [
    'applications:read',
    'credentials:read',
    'credentials:manage',
    'features:read',
    'features:manage',
    'customers:read',
    'customers:manage',
    'licenses:read',
    'licenses:issue',
    'licenses:renew',
    'licenses:suspend',
    'licenses:revoke',
    'licenses:cancel',
    'licenses:reactivate',
  ];
  protected readonly email = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email, Validators.maxLength(254)],
  });
  protected readonly disabled = new FormControl(false, { nonNullable: true });
  protected readonly createForm = new FormGroup({ email: this.email });
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(1000)],
  });
  protected readonly grantForm = new FormGroup({
    applicationId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    capability: new FormControl('applications:read', { nonNullable: true }),
  });
  private createAttempt: { email: string; key: string } | null = null;
  constructor() {
    this.load();
  }
  protected load(cursor: string | null = null) {
    if (cursor === null) this.loadApplications();
    this.http
      .get<{ items: Profile[]; nextCursor: string | null }>('/api/v1/admin/users', {
        params: cursor ? { cursor } : {},
      })
      .pipe(timeout(10000), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.users.set(page.items);
          this.cursor.set(page.nextCursor);
        },
        error: () =>
          this.message.set('No se pudo consultar usuarios. Comprueba tu sesión y permisos.'),
      });
  }
  private loadApplications(cursor: string | null = null) {
    this.http
      .get<{ items: Application[]; nextCursor: string | null }>('/api/v1/admin/applications', {
        params: { pageSize: 100, ...(cursor ? { cursor } : {}) },
      })
      .pipe(timeout(10000), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.applications.set(cursor ? [...this.applications(), ...page.items] : page.items);
          this.applicationCursor.set(page.nextCursor);
        },
        error: () => this.message.set('No se pudo consultar aplicaciones.'),
      });
  }
  protected moreApplications() {
    this.loadApplications(this.applicationCursor());
  }
  protected applicationName(id: string) {
    return this.applications().find((app) => app.id === id)?.name ?? id;
  }
  protected edit(user: Profile) {
    this.selected.set(user);
    this.grants.set([...user.grants]);
    this.disabled.setValue(user.isDisabled);
    this.reason.reset();
  }
  protected add() {
    const grant = this.grantForm.getRawValue();
    if (
      this.grantForm.valid &&
      !this.grants().some(
        (item) =>
          item.applicationId === grant.applicationId && item.capability === grant.capability,
      )
    )
      this.grants.update((values) => [...values, grant]);
  }
  protected remove(grant: Grant) {
    this.grants.update((values) => values.filter((value) => value !== grant));
  }
  protected create() {
    if (this.email.invalid || this.busy()) return;
    if (this.createAttempt?.email !== this.email.value)
      this.createAttempt = { email: this.email.value, key: crypto.randomUUID() };
    this.write(
      'POST',
      '/api/v1/admin/users',
      { email: this.createAttempt.email },
      { 'Idempotency-Key': this.createAttempt.key },
      () => {
        this.createAttempt = null;
        this.createFormDirective()?.resetForm({ email: '' });
        this.message.set(
          'Cuenta pendiente creada. Completa el enrolamiento privado con el titular.',
        );
      },
    );
  }
  protected save() {
    const user = this.selected();
    if (!user || this.reason.invalid || this.busy()) return;
    this.write(
      'PUT',
      '/api/v1/admin/users/' + user.id,
      { isDisabled: this.disabled.value, grants: this.grants(), reason: this.reason.value },
      { 'If-Match': user.etag },
      () => {
        this.selected.set(null);
        this.message.set('Acceso actualizado y sesiones anteriores invalidadas.');
      },
    );
  }
  private write(
    method: string,
    url: string,
    body: object,
    headers: Record<string, string>,
    complete: () => void,
  ) {
    this.busy.set(true);
    this.message.set('');
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap(({ token }) =>
          this.http.request<Profile>(method, url, {
            body,
            headers: { ...headers, 'X-CSRF-TOKEN': token },
          }),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          complete();
          this.load();
        },
        error: (error: HttpErrorResponse) =>
          this.message.set(
            error.status === 412
              ? 'El perfil cambió. Actualiza usuarios y vuelve a editar.'
              : error.status === 409
                ? 'El correo o la operación ya existe. Actualiza la lista antes de reintentar.'
                : 'No se pudo guardar. Comprueba permisos, sesión y datos.',
          ),
      });
  }
}
