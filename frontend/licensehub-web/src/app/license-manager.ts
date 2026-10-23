import {
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  viewChild,
  untracked,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatInputModule } from '@angular/material/input';
import { finalize, switchMap, timeout } from 'rxjs';
import { LicenseLifecycle, ManagedLicense } from './license-lifecycle';

interface Customer {
  id: string;
  subjectId: string;
  displayName: string | null;
  email: string | null;
  etag: string;
}
interface License extends ManagedLicense {
  id: string;
  customerId: string | null;
  state: string;
  isLifetime: boolean;
  expiresAt: string | null;
  features: string[];
}
interface Page<T> {
  items: T[];
  nextCursor: string | null;
}
interface Issued {
  codesRecoverable: boolean;
  items: { license: License; licenseKey: string | null }[];
}

@Component({
  selector: 'app-license-manager',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    LicenseLifecycle,
  ],
  templateUrl: './license-manager.html',
  styles: `
    .tabs {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin: 0 0 24px;
      padding-bottom: 16px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    [aria-current='page'] {
      background: var(--mat-sys-secondary-container);
    }
    form {
      display: grid;
      gap: 12px;
      margin: 16px 0 24px;
    }
    mat-form-field {
      width: 100%;
      min-width: 0;
    }
    p,
    .identifier {
      overflow-wrap: anywhere;
    }
    button {
      white-space: normal;
    }
  `,
})
export class LicenseManager {
  readonly applicationId = input.required<string>();
  readonly initialTab = input<'licenses' | 'customers' | 'issue'>('licenses');
  protected readonly tab = signal<'licenses' | 'customers' | 'issue'>('licenses');
  private readonly lifecycle = viewChild<LicenseLifecycle>('lifecycle');
  isWorking() {
    return this.busy() || !!this.lifecycle()?.isWorking();
  }
  hasPendingWork() {
    return (
      this.isWorking() ||
      this.customerForm.dirty ||
      this.issueForm.dirty ||
      !!this.issued() ||
      !!this.lifecycle()?.hasPendingWork()
    );
  }
  protected stateLabel(state: string) {
    return (
      (
        {
          Pending: 'Pendiente',
          Active: 'Activa',
          Suspended: 'Suspendida',
          Expired: 'Vencida',
          Revoked: 'Revocada',
          Cancelled: 'Cancelada',
        } as Record<string, string>
      )[state] ?? state
    );
  }
  protected openTab(tab: 'licenses' | 'customers' | 'issue') {
    if (this.isWorking() || this.tab() === tab) return;
    if (
      this.lifecycle()?.hasPendingWork() &&
      !confirm('Hay cambios de licencia sin guardar. ¿Quieres cambiar de pestaña?')
    )
      return;
    if (
      this.tab() === 'issue' &&
      this.issued() &&
      !confirm('¿Ya guardaste los códigos? Al salir se ocultarán y no podrás recuperarlos.')
    )
      return;
    if (this.tab() === 'issue') this.issued.set(null);
    this.tab.set(tab);
    if (tab === 'licenses') this.loadLicenses();
    else if (tab === 'customers' || !this.customers().length) this.loadCustomers();
  }
  protected manage(license: License | null) {
    if (this.isWorking()) return;
    if (
      this.lifecycle()?.hasPendingWork() &&
      !confirm('Hay cambios de licencia sin guardar. ¿Quieres salir?')
    )
      return;
    this.managed.set(license);
  }
  protected assignCustomer(customer: Customer) {
    if (this.isWorking()) return;
    this.openTab('issue');
    if (this.tab() !== 'issue') return;
    this.issueForm.patchValue({ customerId: customer.id, quantity: 1 });
    this.issueForm.markAsDirty();
  }
  protected hideCodes() {
    if (confirm('¿Guardaste todos los códigos? No podrás recuperarlos después.'))
      this.issued.set(null);
  }

  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly catalog = signal<{ code: string; name: string; isActive: boolean }[]>([]);
  protected readonly catalogMessage = signal('');
  protected featureSelected(code: string) {
    return this.issueForm.controls.features.value
      .split(',')
      .map((x) => x.trim())
      .includes(code);
  }
  protected toggleFeature(code: string, checked: boolean) {
    const codes = this.issueForm.controls.features.value
      .split(',')
      .map((x) => x.trim())
      .filter((x) => !!x && x !== code);
    if (checked) codes.push(code);
    this.issueForm.controls.features.setValue(codes.join(', '));
    this.issueForm.controls.features.markAsDirty();
  }
  protected loadCatalog() {
    this.http
      .get<Page<{ code: string; name: string; isActive: boolean }>>(
        '/api/v1/admin/applications/' + this.applicationId() + '/features',
        { params: { pageSize: 100 } },
      )
      .pipe(timeout(10000), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => {
          this.catalog.set(p.items.filter((x) => x.isActive));
          this.catalogMessage.set(
            p.nextCursor
              ? 'Se muestran las primeras funciones; puedes introducir otros códigos activos en Opciones avanzadas.'
              : '',
          );
        },
        error: () =>
          this.catalogMessage.set(
            'No se pudo cargar el catálogo. Revisa Conexión y funciones o introduce los códigos en Opciones avanzadas.',
          ),
      });
  }
  protected readonly customers = signal<Customer[]>([]);
  protected readonly licenses = signal<License[]>([]);
  protected readonly customerCursor = signal<string | null>(null);
  protected readonly licenseCursor = signal<string | null>(null);
  protected readonly selectedCustomer = signal<Customer | null>(null);
  protected readonly issued = signal<Issued | null>(null);
  protected readonly managed = signal<License | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly customerSearch = new FormControl('', { nonNullable: true });
  private customerQuery = '';
  protected readonly state = new FormControl('', { nonNullable: true });
  protected readonly states = ['Pending', 'Active', 'Suspended', 'Expired', 'Revoked', 'Cancelled'];
  protected readonly customerForm = new FormGroup({
    subjectId: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/)],
    }),
    displayName: new FormControl('', { nonNullable: true, validators: Validators.maxLength(200) }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.email, Validators.maxLength(254)],
    }),
  });
  protected readonly issueForm = new FormGroup({
    quantity: new FormControl(1, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(100)],
    }),
    startMode: new FormControl('OnActivation', { nonNullable: true }),
    kind: new FormControl('Duration', { nonNullable: true }),
    value: new FormControl(30, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(1000000)],
    }),
    unit: new FormControl('Day', { nonNullable: true }),
    until: new FormControl('', { nonNullable: true }),
    customerId: new FormControl('', { nonNullable: true }),
    features: new FormControl('', { nonNullable: true }),
  });
  private customerIntent: { body: string; key: string } | null = null;
  private issueIntent: { body: string; key: string } | null = null;

  constructor() {
    effect(() => {
      this.applicationId();
      this.customers.set([]);
      this.licenses.set([]);
      this.issued.set(null);
      this.managed.set(null);
      this.customerCursor.set(null);
      this.licenseCursor.set(null);
      this.customerIntent = null;
      this.issueIntent = null;
      this.newCustomer();
      this.issueForm.reset({
        quantity: 1,
        startMode: 'OnActivation',
        kind: 'Duration',
        value: 30,
        unit: 'Day',
        until: '',
        customerId: '',
        features: '',
      });
      this.message.set('');
      const tab = this.initialTab();
      this.tab.set(tab);
      untracked(() => this.loadCatalog());
      untracked(() => (tab === 'licenses' ? this.loadLicenses() : this.loadCustomers()));
    });
    this.destroyRef.onDestroy(() => this.issued.set(null));
  }

  protected loadCustomers(cursor: string | null = null) {
    if (this.busy()) return;
    if (!cursor) this.customerQuery = this.customerSearch.value.trim();
    this.busy.set(true);
    this.http
      .get<Page<Customer>>('/api/v1/admin/customers', {
        params: {
          applicationId: this.applicationId(),
          pageSize: 25,
          ...(this.customerQuery ? { query: this.customerQuery } : {}),
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
          this.customers.set(p.items);
          this.customerCursor.set(p.nextCursor);
        },
        error: (e) => this.failure(e),
      });
  }
  protected loadLicenses(cursor: string | null = null) {
    if (this.busy()) return;
    this.busy.set(true);
    this.http
      .get<Page<License>>('/api/v1/admin/licenses', {
        params: {
          applicationId: this.applicationId(),
          pageSize: 25,
          ...(this.state.value ? { state: this.state.value } : {}),
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
          this.licenses.set(p.items);
          this.licenseCursor.set(p.nextCursor);
        },
        error: (e) => this.failure(e),
      });
  }
  protected edit(customer: Customer) {
    this.selectedCustomer.set(customer);
    this.customerForm.reset({
      subjectId: customer.subjectId,
      displayName: customer.displayName ?? '',
      email: customer.email ?? '',
    });
    this.customerForm.controls.subjectId.disable();
    this.customerIntent = null;
  }
  protected newCustomer() {
    this.selectedCustomer.set(null);
    this.customerForm.controls.subjectId.enable();
    this.customerForm.reset({ subjectId: '', displayName: '', email: '' });
    this.customerIntent = null;
  }
  protected saveCustomer() {
    if (this.busy() || this.customerForm.invalid) return;
    const value = this.customerForm.getRawValue(),
      selected = this.selectedCustomer();
    const body = selected
      ? { displayName: value.displayName || null, email: value.email || null }
      : {
          applicationId: this.applicationId(),
          subjectId: value.subjectId,
          displayName: value.displayName || null,
          email: value.email || null,
        };
    const fingerprint = JSON.stringify({ selected: selected?.id, etag: selected?.etag, body });
    if (this.customerIntent?.body !== fingerprint)
      this.customerIntent = { body: fingerprint, key: crypto.randomUUID() };
    const headers = {
      'Idempotency-Key': this.customerIntent.key,
      ...(selected ? { 'If-Match': selected.etag } : {}),
    };
    this.busy.set(true);
    this.message.set('');
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap((csrf) =>
          selected
            ? this.http.put<Customer>('/api/v1/admin/customers/' + selected.id, body, {
                headers: { ...headers, 'X-CSRF-TOKEN': csrf.token },
              })
            : this.http.post<Customer>('/api/v1/admin/customers', body, {
                headers: { ...headers, 'X-CSRF-TOKEN': csrf.token },
              }),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (customer) => {
          this.edit(customer);
          this.message.set('Cliente guardado. Actualiza la lista para consultarlo.');
        },
        error: (e) => this.failure(e),
      });
  }
  protected issue() {
    if (this.busy() || this.issueForm.invalid) return;
    if (
      this.issued() &&
      !confirm('¿Ya guardaste los códigos anteriores? Este envío ocultará los códigos visibles.')
    )
      return;
    const value = this.issueForm.getRawValue();
    if (
      value.kind === 'Until' &&
      (value.startMode !== 'OnIssue' ||
        !value.until ||
        Number.isNaN(new Date(value.until).getTime()))
    ) {
      this.message.set('Selecciona inicio al emitir y una fecha válida.');
      return;
    }
    const term =
      value.kind === 'Lifetime'
        ? { kind: 'Lifetime' }
        : value.kind === 'Until'
          ? { kind: 'Until', until: new Date(value.until).toISOString() }
          : { kind: 'Duration', duration: { unit: value.unit, value: value.value } };
    const body = {
      applicationId: this.applicationId(),
      quantity: value.quantity,
      startMode: value.startMode,
      term,
      features: value.features
        .split(',')
        .map((x) => x.trim())
        .filter(Boolean),
      customerId: value.customerId.trim() || null,
    };
    const fingerprint = JSON.stringify(body);
    if (this.issueIntent?.body !== fingerprint)
      this.issueIntent = { body: fingerprint, key: crypto.randomUUID() };
    const key = this.issueIntent.key;
    this.busy.set(true);
    this.message.set('');
    this.issued.set(null);
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap((csrf) =>
          this.http.post<Issued>('/api/v1/admin/licenses', body, {
            headers: { 'Idempotency-Key': key, 'X-CSRF-TOKEN': csrf.token },
          }),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (batch) => {
          this.issued.set(batch);
          this.issueForm.markAsPristine();
          this.message.set(
            'Operación confirmada. Guarda los códigos antes de preparar otra emisión.',
          );
        },
        error: (e) => this.failure(e),
      });
  }
  protected newIssue() {
    if (
      this.busy() ||
      (this.issued() && !confirm('¿Ya guardaste los códigos? No podrás recuperarlos después.'))
    )
      return;
    this.issued.set(null);
    this.issueIntent = null;
    this.message.set('Nueva emisión preparada. El siguiente envío creará otro lote.');
  }
  private failure(error: unknown) {
    const code = error instanceof HttpErrorResponse ? error.error?.code : undefined;
    const messages: Record<string, string> = {
      validation_failed: 'Revisa los datos, la vigencia y los códigos del catálogo.',
      not_found: 'Recurso inexistente o sin permiso para esta aplicación.',
      precondition_failed: 'El contacto cambió. Actualiza la lista antes de editar.',
      conflict: 'El cliente ya existe o la operación entra en conflicto.',
      idempotency_conflict: 'La clave de reintento tiene otros datos.',
      authentication_required: 'La sesión venció. Vuelve a iniciar sesión.',
    };
    this.message.set(
      messages[code] ??
        'No se pudo confirmar la operación. Puedes reintentar con los mismos datos.',
    );
  }
}
