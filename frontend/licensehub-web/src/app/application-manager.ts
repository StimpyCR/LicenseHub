import { Component, DestroyRef, computed, inject, input, signal, viewChild } from '@angular/core';
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
import { PaymentManager } from './payment-manager';
import { NotificationManager } from './notification-manager';
import { AdminReports } from './admin-reports';

interface ApplicationView {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  etag: string;
}

type WorkspaceSection =
  'overview' | 'licenses' | 'payments' | 'notifications' | 'integration' | 'reports';

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
    PaymentManager,
    NotificationManager,
    AdminReports,
  ],
  templateUrl: './application-manager.html',
  styleUrl: './application-manager.scss',
})
export class ApplicationManager {
  protected readonly workspace = signal<ApplicationView | null>(null);
  protected readonly section = signal<WorkspaceSection>('overview');
  protected readonly licenseTab = signal<'licenses' | 'customers' | 'issue'>('licenses');
  protected readonly showEditor = signal(false);
  protected readonly query = signal('');
  protected readonly loading = signal(false);
  protected readonly onLaterPage = signal(false);
  private readonly licenseComponent = viewChild<LicenseManager>('licenseManager');
  private readonly integrationComponent = viewChild<ApplicationDetails>('integrationDetails');
  private readonly paymentComponent = viewChild<PaymentManager>('paymentManager');
  private readonly notificationComponent = viewChild<NotificationManager>('notificationManager');
  protected readonly sections: { id: WorkspaceSection; label: string }[] = [
    { id: 'overview', label: 'Inicio' },
    { id: 'licenses', label: 'Clientes y licencias' },
    { id: 'payments', label: 'Pagos' },
    { id: 'notifications', label: 'Correos y avisos' },
    { id: 'integration', label: 'Conexión y funciones' },
    { id: 'reports', label: 'Informes' },
  ];
  readonly canManage = input(false);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly applicationFormDirective = viewChild<FormGroupDirective>(
    'applicationFormDirective',
  );
  protected readonly applications = signal<ApplicationView[]>([]);
  protected readonly visibleApplications = computed(() => {
    const query = this.query().trim().toLocaleLowerCase();
    return this.applications().filter((app) =>
      (app.name + ' ' + app.slug).toLocaleLowerCase().includes(query),
    );
  });
  protected readonly cursor = signal<string | null>(null);
  protected readonly selected = signal<ApplicationView | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  private attempt: { fingerprint: string; key: string } | null = null;
  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120), Validators.pattern(/\S/)],
    }),
    slug: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[a-z][a-z0-9-]{0,63}$/)],
    }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    this.form.controls.name.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((name) => {
        if (!this.selected() && this.form.controls.slug.pristine) {
          let slug = name
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '')
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '');
          if (/^[0-9]/.test(slug)) slug = 'app-' + slug;
          this.form.controls.slug.setValue(slug.slice(0, 64).replace(/-+$/g, ''), {
            emitEvent: false,
          });
        }
      });
    this.load();
  }
  private children() {
    return [
      this.licenseComponent(),
      this.integrationComponent(),
      this.paymentComponent(),
      this.notificationComponent(),
    ];
  }
  hasPendingWork(): boolean {
    return (
      this.busy() ||
      (this.showEditor() && this.form.dirty) ||
      this.children().some((child) => child?.hasPendingWork())
    );
  }
  canLeave(): boolean {
    if (this.busy() || this.children().some((child) => child?.isWorking())) {
      this.message.set('Espera a que termine la operación antes de cambiar de sección.');
      return false;
    }
    return (
      !this.hasPendingWork() ||
      window.confirm(
        'Hay cambios sin guardar o códigos que solo se muestran una vez. Guárdalos antes de salir. ¿Quieres salir de todos modos?',
      )
    );
  }
  protected open(app: ApplicationView) {
    if (!this.canLeave()) return;
    this.reset();
    this.workspace.set(app);
    this.section.set('overview');
    this.message.set('');
  }
  protected backToApplications() {
    if (!this.canLeave()) return;
    this.reset();
    this.workspace.set(null);
    this.message.set('');
  }
  protected navigate(section: WorkspaceSection) {
    if (this.section() === section && !this.showEditor()) return;
    if (!this.canLeave()) return;
    this.reset();
    this.section.set(section);
    this.message.set('');
  }
  protected openLicenses(tab: 'licenses' | 'customers' | 'issue') {
    this.licenseTab.set(tab);
    this.navigate('licenses');
  }
  protected startCreate() {
    if (!this.canLeave()) return;
    this.reset();
    this.showEditor.set(true);
    this.message.set('');
  }
  protected cancelEditor() {
    if (this.canLeave()) this.reset();
  }

  protected load(cursor: string | null = null) {
    if (this.loading()) return;
    this.loading.set(true);
    this.http
      .get<{ items: ApplicationView[]; nextCursor: string | null }>('/api/v1/admin/applications', {
        params: cursor ? { cursor } : {},
      })
      .pipe(
        timeout(10000),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (page) => {
          this.applications.set(page.items);
          this.onLaterPage.set(!!cursor);
          this.cursor.set(page.nextCursor);
        },
        error: () => this.message.set('No se pudo consultar la lista. Comprueba tu sesión.'),
      });
  }
  protected edit(app: ApplicationView) {
    if (!this.canLeave()) return;
    this.showEditor.set(true);
    this.message.set('');
    this.selected.set(app);
    this.form.setValue({ name: app.name, slug: app.slug, isActive: app.isActive });
    this.form.controls.slug.disable();
    this.attempt = null;
  }
  protected reset() {
    this.showEditor.set(false);
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
        next: (saved) => {
          if (this.workspace()?.id === saved.id) this.workspace.set(saved);
          this.reset();
          this.message.set('Aplicación guardada.');
          this.load();
        },
        error: (error: HttpErrorResponse) =>
          this.message.set(
            error.status === 412
              ? 'La aplicación cambió. Actualiza la lista y vuelve a editar.'
              : error.status === 409
                ? 'No se pudo crear: ese identificador puede estar reservado. Prueba con otro o actualiza la lista.'
                : 'No se pudo guardar. Reintenta los mismos datos o comprueba tus permisos.',
          ),
      });
  }
}
