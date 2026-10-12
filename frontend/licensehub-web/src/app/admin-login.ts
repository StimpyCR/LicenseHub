import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { finalize } from 'rxjs';
import { AdminAuthService, AdminSession } from './admin-auth.service';
import { ApplicationManager } from './application-manager';

@Component({
  selector: 'app-admin-login',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    ApplicationManager,
  ],
  template: `
    @if (session(); as current) {
      <p>Sesión administrativa: {{ current.email }}</p>
      <p>MFA verificado.</p>
      <button mat-stroked-button [disabled]="busy()" (click)="logout()">Cerrar sesión</button>
      @defer (on immediate) {
        <app-application-manager [canManage]="current.capabilities.includes('superadmin')" />
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

  constructor() {
    this.auth
      .session()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (session) => this.session.set(session),
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
          this.session.set(session);
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
        next: () => this.session.set(null),
        error: () => {
          this.auth
            .session()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (session) => {
                this.session.set(session);
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
