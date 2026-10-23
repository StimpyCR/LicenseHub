import { Component, DestroyRef, inject, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { finalize } from 'rxjs';
import { AdminAuthService, AdminSession } from './admin-auth.service';
import { ApplicationManager } from './application-manager';
import { AdminUserManager } from './admin-user-manager';

@Component({
  selector: 'app-admin-login',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    ApplicationManager,
    AdminUserManager,
  ],
  template: `
    @if (session(); as current) {
      <div class="session-bar">
        <div>
          <strong>{{ current.email }}</strong
          ><span class="session-badge">MFA verificado.</span>
        </div>
        <button mat-stroked-button [disabled]="busy()" (click)="logout()">Cerrar sesión</button>
      </div>
      @if (current.capabilities.includes('superadmin')) {
        <nav class="admin-navigation" aria-label="Administración general">
          <button
            mat-button
            [attr.aria-current]="panel() === 'applications' ? 'page' : null"
            (click)="changePanel('applications')"
          >
            Mis aplicaciones</button
          ><button
            mat-button
            [attr.aria-current]="panel() === 'users' ? 'page' : null"
            (click)="changePanel('users')"
          >
            Administradores
          </button>
        </nav>
      }
      @if (panel() === 'applications' || !current.capabilities.includes('superadmin')) {
        @defer (on immediate) {
          <app-application-manager
            #applicationManager
            [canManage]="current.capabilities.includes('superadmin')"
          />
        }
      } @else {
        <section class="users-panel">
          <h2>Administradores</h2>
          <p>Gestiona quién puede entrar al panel y a qué aplicaciones tiene acceso.</p>
          @defer (on immediate) {
            <app-admin-user-manager #adminUsers />
          }
        </section>
      }
    } @else if (challenge()) {
      <p>Introduce el código de seis dígitos de tu autenticador.</p>
      <form [formGroup]="mfaForm" (ngSubmit)="complete()">
        <mat-form-field appearance="outline">
          <mat-label>Código de autenticador</mat-label>
          <input
            matInput
            formControlName="code"
            inputmode="numeric"
            autocomplete="one-time-code"
            maxlength="6"
          />
          @if (mfaForm.controls.code.invalid && mfaForm.controls.code.touched) {
            <mat-error>Introduce seis dígitos.</mat-error>
          }
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy() || mfaForm.invalid">
          Verificar MFA
        </button>
        <button mat-button type="button" [disabled]="busy()" (click)="back()">
          Volver al acceso
        </button>
      </form>
    } @else {
      <p>Acceso privado para administradores.</p>
      <form [formGroup]="loginForm" (ngSubmit)="login()">
        <mat-form-field appearance="outline">
          <mat-label>Correo electrónico</mat-label>
          <input
            matInput
            formControlName="email"
            type="email"
            autocomplete="username"
            maxlength="254"
          />
          @if (loginForm.controls.email.invalid && loginForm.controls.email.touched) {
            <mat-error>Introduce un correo válido.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Contraseña</mat-label>
          <input
            matInput
            formControlName="password"
            type="password"
            autocomplete="current-password"
            maxlength="1024"
          />
          @if (loginForm.controls.password.invalid && loginForm.controls.password.touched) {
            <mat-error>Introduce tu contraseña.</mat-error>
          }
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy() || loginForm.invalid">
          Continuar con MFA
        </button>
      </form>
    }
    @if (busy()) {
      <p role="status">Procesando…</p>
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
  `,
  styles: `
    .session-bar,
    .session-bar > div {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }
    .session-bar {
      justify-content: space-between;
      padding-bottom: 20px;
      margin-bottom: 20px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .session-bar strong {
      overflow-wrap: anywhere;
    }
    .session-badge {
      font-size: 12px;
      color: var(--mat-sys-on-tertiary-container);
      background: var(--mat-sys-tertiary-container);
      padding: 4px 10px;
      border-radius: 20px;
    }
    .admin-navigation {
      display: flex;
      gap: 8px;
      margin-bottom: 28px;
      flex-wrap: wrap;
    }
    .admin-navigation button[aria-current] {
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .users-panel {
      padding: 24px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 20px;
    }

    form {
      display: grid;
      gap: 12px;
    }
    mat-form-field {
      width: 100%;
      min-width: 0;
    }
    button {
      white-space: normal;
    }
    [role='alert'] {
      color: var(--mat-sys-error);
    }
  `,
})
export class AdminLogin {
  private readonly auth = inject(AdminAuthService);
  private readonly destroyRef = inject(DestroyRef);
  readonly sessionChanged = output<AdminSession | null>();
  protected readonly panel = signal<'applications' | 'users'>('applications');
  private readonly manager = viewChild<ApplicationManager>('applicationManager');
  private readonly users = viewChild<AdminUserManager>('adminUsers');
  protected readonly session = signal<AdminSession | null>(null);
  protected readonly busy = signal(true);
  protected readonly challenge = signal(false);
  protected readonly error = signal('');
  protected readonly loginForm = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(254)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(1024)],
    }),
  });
  protected readonly mfaForm = new FormGroup({
    code: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[0-9]{6}$/)],
    }),
  });

  protected changePanel(panel: 'applications' | 'users') {
    if (this.panel() === panel) return;
    if (this.manager() && !this.manager()!.canLeave()) return;
    if (this.users()?.isWorking()) {
      this.error.set('Espera a que termine la operación.');
      return;
    }
    if (
      this.users()?.hasPendingWork() &&
      !window.confirm('Hay cambios sin guardar. ¿Quieres cambiar de sección?')
    )
      return;
    this.panel.set(panel);
  }
  private setSession(session: AdminSession | null) {
    this.session.set(session);
    this.sessionChanged.emit(session);
    if (!session) this.panel.set('applications');
  }
  constructor() {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (this.manager()?.hasPendingWork() || this.users()?.hasPendingWork()) {
        event.preventDefault();
        event.returnValue = '';
      }
    };
    window.addEventListener('beforeunload', beforeUnload);
    this.destroyRef.onDestroy(() => window.removeEventListener('beforeunload', beforeUnload));
    this.auth
      .session()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (session) => this.setSession(session),
        error: () => this.error.set('No se pudo comprobar la sesión. Intenta más tarde.'),
      });
  }

  protected login() {
    if (this.busy() || this.loginForm.invalid) return;
    this.busy.set(true);
    this.error.set('');
    const { email, password } = this.loginForm.getRawValue();
    this.auth
      .login(email, password)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => this.challenge.set(true),
        error: () =>
          this.error.set(
            'No se pudo iniciar el acceso. Revisa tus credenciales o intenta más tarde.',
          ),
      });
    this.loginForm.controls.password.reset();
  }

  protected complete() {
    if (this.busy() || this.mfaForm.invalid) return;
    this.busy.set(true);
    this.error.set('');
    this.auth
      .complete(this.mfaForm.getRawValue().code)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (session) => {
          this.setSession(session);
          this.challenge.set(false);
        },
        error: () =>
          this.error.set(
            'No se pudo verificar el código. Puede haber caducado, sido usado o haberse bloqueado el acceso.',
          ),
      });
    this.mfaForm.reset();
  }

  protected logout() {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.auth
      .logout()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => this.setSession(null),
        error: () => {
          this.auth
            .session()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (session) => {
                this.setSession(session);
                this.error.set(
                  session
                    ? 'No se pudo cerrar la sesión. Intenta de nuevo.'
                    : 'La sesión ya no está disponible.',
                );
              },
              error: () =>
                this.error.set('No se pudo confirmar el cierre de sesión. Intenta de nuevo.'),
            });
        },
      });
  }

  protected back() {
    this.challenge.set(false);
    this.error.set('');
    this.mfaForm.reset();
  }
}
