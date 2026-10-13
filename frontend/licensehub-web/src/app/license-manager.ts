import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
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
    LicenseLifecycle,
  ],
  template: `
    <h4>Clientes</h4>
    <p>
      El identificador de negocio lo determina el backend de la aplicación. No uses un ID elegido
      por el navegador.
    </p>
    <mat-form-field
      ><mat-label>Buscar cliente</mat-label
      ><input matInput [formControl]="customerSearch" maxlength="120"
    /></mat-form-field>
    <button mat-button [disabled]="busy()" (click)="loadCustomers()">
      Buscar / actualizar clientes
    </button>
    @for (customer of customers(); track customer.id) {
      <p>
        {{ customer.subjectId }} · {{ customer.displayName || 'Sin nombre' }} ·
        {{ customer.email || 'Sin correo' }}
      </p>
      <button mat-button [disabled]="busy()" (click)="edit(customer)">Editar contacto</button>
      <button
        mat-button
        [disabled]="busy()"
        (click)="issueForm.controls.customerId.setValue(customer.id)"
      >
        Preasignar licencia
      </button>
    } @empty {
      <p>No hay clientes cargados.</p>
    }
    @if (customerCursor()) {
      <button mat-button [disabled]="busy()" (click)="loadCustomers(customerCursor())">
        Clientes siguientes
      </button>
    }
    <form [formGroup]="customerForm" (ngSubmit)="saveCustomer()">
      <mat-form-field
        ><mat-label>Identificador de negocio</mat-label
        ><input matInput formControlName="subjectId" maxlength="128"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Nombre del cliente</mat-label
        ><input matInput formControlName="displayName" maxlength="200"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Correo del cliente</mat-label
        ><input matInput formControlName="email" type="email" maxlength="254"
      /></mat-form-field>
      <button mat-flat-button [disabled]="busy() || customerForm.invalid" type="submit">
        {{ selectedCustomer() ? 'Guardar contacto' : 'Registrar cliente' }}
      </button>
      <button mat-button [disabled]="busy()" type="button" (click)="newCustomer()">
        Nuevo cliente
      </button>
    </form>

    <h4>Emitir licencias</h4>
    <p>
      Los códigos completos se muestran una sola vez. Un reintento confirma la emisión sin
      recuperarlos. Guarda los códigos antes de ocultarlos.
    </p>
    <form [formGroup]="issueForm" (ngSubmit)="issue()">
      <mat-form-field
        ><mat-label>Cantidad (1–100)</mat-label
        ><input matInput type="number" formControlName="quantity" min="1" max="100"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Inicio de vigencia</mat-label
        ><select matNativeControl formControlName="startMode">
          <option value="OnActivation">Al activar</option>
          <option value="OnIssue">Al emitir</option>
        </select></mat-form-field
      >
      <mat-form-field
        ><mat-label>Tipo de vigencia</mat-label
        ><select matNativeControl formControlName="kind">
          <option value="Duration">Duración</option>
          <option value="Until">Fecha específica</option>
          <option value="Lifetime">Vitalicia</option>
        </select></mat-form-field
      >
      @if (issueForm.controls.kind.value === 'Duration') {
        <mat-form-field
          ><mat-label>Duración</mat-label
          ><input matInput formControlName="value" type="number" min="1" max="1000000"
        /></mat-form-field>
        <mat-form-field
          ><mat-label>Unidad</mat-label
          ><select matNativeControl formControlName="unit">
            <option value="Minute">Minutos</option>
            <option value="Hour">Horas</option>
            <option value="Day">Días</option>
            <option value="Month">Meses calendario</option>
            <option value="Year">Años calendario</option>
          </select></mat-form-field
        >
      }
      @if (issueForm.controls.kind.value === 'Until') {
        <mat-form-field
          ><mat-label>Fecha de vencimiento local</mat-label
          ><input matInput formControlName="until" type="datetime-local" /><mat-hint
            >Se envía en UTC. Requiere inicio al emitir.</mat-hint
          ></mat-form-field
        >
      }
      <mat-form-field
        ><mat-label>Cliente preasignado (UUID opcional)</mat-label
        ><input matInput formControlName="customerId" /><mat-hint
          >Preasignación admite una licencia por emisión.</mat-hint
        ></mat-form-field
      >
      <mat-form-field
        ><mat-label>Códigos de funcionalidades, separados por coma</mat-label
        ><input matInput formControlName="features" maxlength="6500" /><mat-hint
          >Solo códigos activos del catálogo de esta aplicación.</mat-hint
        ></mat-form-field
      >
      <button mat-flat-button [disabled]="busy() || issueForm.invalid" type="submit">
        Emitir licencias
      </button>
      <button mat-button [disabled]="busy()" type="button" (click)="newIssue()">
        Preparar otra emisión
      </button>
    </form>
    @if (issued(); as batch) {
      <p>
        {{
          batch.codesRecoverable
            ? 'Emisión completada. Códigos disponibles temporalmente.'
            : 'Emisión ya registrada; los códigos no son recuperables.'
        }}
      </p>
      @for (item of batch.items; track item.license.id) {
        <p class="identifier">Licencia {{ item.license.id }} · {{ item.license.state }}</p>
        @if (item.licenseKey) {
          <mat-form-field
            ><mat-label>Código de licencia {{ $index + 1 }}</mat-label
            ><input matInput readonly autocomplete="off" [value]="item.licenseKey"
          /></mat-form-field>
        }
      }
      <button mat-stroked-button (click)="issued.set(null)">Ocultar códigos</button>
    }

    <h4>Licencias de la aplicación</h4>
    <mat-form-field
      ><mat-label>Filtrar estado</mat-label
      ><select matNativeControl [formControl]="state">
        <option value="">Todos</option>
        @for (value of states; track value) {
          <option [value]="value">{{ value }}</option>
        }
      </select></mat-form-field
    >
    <button mat-button [disabled]="busy()" (click)="loadLicenses()">Consultar licencias</button>
    @for (license of licenses(); track license.id) {
      <p class="identifier">{{ license.id }} · {{ license.state }}</p>
      <p>
        {{
          license.isLifetime
            ? 'Vitalicia'
            : 'Vencimiento: ' + ((license.expiresAt | date: 'medium') || 'Comienza al activar')
        }}
      </p>
      <p>Funcionalidades: {{ license.features.join(', ') || 'Ninguna' }}</p>
      <button mat-button [disabled]="busy()" (click)="managed.set(license)">
        Administrar esta licencia
      </button>
    } @empty {
      <p>No hay licencias cargadas.</p>
    }
    @if (licenseCursor()) {
      <button mat-button [disabled]="busy()" (click)="loadLicenses(licenseCursor())">
        Licencias siguientes
      </button>
    }
    @for (item of managed() ? [managed()!] : []; track item.id) {
      <button mat-button (click)="managed.set(null)">Cerrar administración de licencia</button>
      @defer (on immediate) {
        <app-license-lifecycle [license]="item" />
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
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
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
    value: new FormControl(2, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(1000000)],
    }),
    unit: new FormControl('Minute', { nonNullable: true }),
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
        value: 2,
        unit: 'Minute',
        until: '',
        customerId: '',
        features: '',
      });
      this.message.set('');
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
          this.message.set(
            'Operación confirmada. Guarda los códigos antes de preparar otra emisión.',
          );
        },
        error: (e) => this.failure(e),
      });
  }
  protected newIssue() {
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
