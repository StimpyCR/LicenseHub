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

interface Settings {
  offsetSeconds: number[];
  shortDurationOffsetSeconds: number[];
  emailEnabled: boolean;
  inAppEnabled: boolean;
}
interface Template {
  eventType: string;
  locale: string;
  subject: string;
  body: string;
  version: string;
}
interface Log {
  id: string;
  eventType: string;
  channel: string;
  scheduledAt: string;
  state: string;
  attempts: number;
  lastError: string | null;
}

@Component({
  selector: 'app-notification-manager',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
  ],
  template: `
    <h4>Notificaciones</h4>
    <p>
      Las alertas se procesan por tareas persistentes. Los correos fallidos quedan registrados y se
      reintentan; no equivalen a enviados.
    </p>
    <button mat-button [disabled]="busy()" (click)="loadSettings()">Consultar configuración</button>
    <form [formGroup]="settings" (ngSubmit)="saveSettings()">
      <mat-form-field
        ><mat-label>Anticipación en segundos, separada por comas</mat-label
        ><input matInput formControlName="offsets" /><mat-hint
          >7, 3 y 1 día: 604800,259200,86400</mat-hint
        ></mat-form-field
      >
      <mat-form-field
        ><mat-label>Anticipación para licencias cortas, en segundos</mat-label
        ><input matInput formControlName="shortOffsets"
      /></mat-form-field>
      <mat-checkbox formControlName="email">Correo habilitado</mat-checkbox>
      <mat-checkbox formControlName="inApp">Avisos en la aplicación habilitados</mat-checkbox>
      <p>
        Conserva al menos un canal. Los intervalos se aplican a nuevas programaciones; deshabilitar
        un canal detiene entregas pendientes.
      </p>
      <button mat-flat-button type="submit" [disabled]="busy()">Guardar configuración</button>
    </form>
    <h4>Plantillas de texto plano</h4>
    <p>
      Variables permitidas: {{ variables }}. No se interpreta HTML. El idioma de entrega lo
      configura el operador del servidor; por defecto es es.
    </p>
    <button mat-button [disabled]="busy()" (click)="loadTemplates()">Consultar plantillas</button>
    @for (template of templates(); track template.eventType + ':' + template.locale) {
      <p>{{ template.eventType }} · {{ template.locale }} · versión {{ template.version }}</p>
      <button mat-button [disabled]="busy()" (click)="edit(template)">Editar plantilla</button>
    }
    @if (templateCursor()) {
      <button mat-button [disabled]="busy()" (click)="loadTemplates(templateCursor())">
        Plantillas siguientes
      </button>
    }
    <form [formGroup]="template" (ngSubmit)="saveTemplate()">
      <mat-form-field
        ><mat-label>Evento</mat-label
        ><select matNativeControl formControlName="eventType">
          @for (event of events; track event) {
            <option [value]="event">{{ event }}</option>
          }
        </select></mat-form-field
      >
      <mat-form-field
        ><mat-label>Idioma de plantilla</mat-label
        ><input matInput formControlName="locale" maxlength="5"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Asunto de plantilla</mat-label
        ><input matInput formControlName="subject" maxlength="200"
      /></mat-form-field>
      <mat-form-field
        ><mat-label>Texto de plantilla</mat-label
        ><textarea matInput formControlName="body" maxlength="20000" rows="5"></textarea>
      </mat-form-field>
      <button mat-flat-button type="submit" [disabled]="busy() || template.invalid">
        Guardar plantilla
      </button>
      <button mat-button type="button" [disabled]="busy()" (click)="newTemplate()">
        Nueva plantilla
      </button>
    </form>
    <h4>Registro de avisos y envíos</h4>
    <button mat-button [disabled]="busy()" (click)="loadLogs()">Consultar envíos</button>
    @for (log of logs(); track log.id) {
      <p>
        {{ log.eventType }} · {{ log.channel }} · {{ log.state }} ·
        {{ log.scheduledAt | date: 'medium' }} · intentos {{ log.attempts }}
      </p>
      @if (log.lastError) {
        <p>Error: {{ log.lastError }}</p>
      }
    } @empty {
      <p>No hay registros cargados.</p>
    }
    @if (logCursor()) {
      <button mat-button [disabled]="busy()" (click)="loadLogs(logCursor())">
        Envíos siguientes
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
    }
  `,
})
export class NotificationManager {
  isWorking(): boolean {
    return this.busy();
  }
  hasPendingWork(): boolean {
    return this.busy() || this.settings.dirty || this.template.dirty;
  }

  readonly applicationId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly templates = signal<Template[]>([]);
  protected readonly templateCursor = signal<string | null>(null);
  protected readonly logs = signal<Log[]>([]);
  protected readonly logCursor = signal<string | null>(null);
  protected readonly events = [
    'Activated',
    'ExpiryApproaching',
    'Expired',
    'Renewed',
    'Suspended',
    'Revoked',
  ];
  protected readonly variables =
    '{applicationName}, {subjectId}, {licenseId}, {expiresAt}, {eventType}';
  protected readonly settings = new FormGroup({
    offsets: new FormControl('604800,259200,86400', { nonNullable: true }),
    shortOffsets: new FormControl('60', { nonNullable: true }),
    email: new FormControl(true, { nonNullable: true }),
    inApp: new FormControl(true, { nonNullable: true }),
  });
  protected readonly template = new FormGroup({
    eventType: new FormControl('Activated', { nonNullable: true }),
    locale: new FormControl('es', {
      nonNullable: true,
      validators: Validators.pattern(/^[a-z]{2}(-[A-Z]{2})?$/),
    }),
    subject: new FormControl('', { nonNullable: true, validators: Validators.required }),
    body: new FormControl('', { nonNullable: true, validators: Validators.required }),
  });
  private settingsEtag = '"0"';
  private templateVersion = '0';
  private intent: { body: string; key: string; etag: string } | null = null;
  protected loadSettings() {
    if (this.busy()) return;
    this.busy.set(true);
    this.http
      .get<Settings>(this.base() + '/notification-settings', { observe: 'response' })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (response) => {
          const value = response.body;
          if (!value) return;
          this.settingsEtag = response.headers.get('ETag') ?? '"0"';
          this.settings.setValue({
            offsets: value.offsetSeconds.join(','),
            shortOffsets: value.shortDurationOffsetSeconds.join(','),
            email: value.emailEnabled,
            inApp: value.inAppEnabled,
          });
          this.intent = null;
        },
        error: (e) => this.failure(e),
      });
  }
  protected saveSettings() {
    if (this.busy()) return;
    const value = this.settings.getRawValue();
    const offsets = this.parse(value.offsets),
      short = this.parse(value.shortOffsets);
    if (!offsets || !short) {
      this.message.set('Usa segundos enteros positivos separados por comas.');
      return;
    }
    const body = {
      offsetSeconds: offsets,
      shortDurationOffsetSeconds: short,
      emailEnabled: value.email,
      inAppEnabled: value.inApp,
    };
    const fingerprint = JSON.stringify({ path: 'settings', body });
    if (this.intent?.body !== fingerprint)
      this.intent = { body: fingerprint, key: crypto.randomUUID(), etag: this.settingsEtag };
    const intent = this.intent;
    this.busy.set(true);
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap((csrf) =>
          this.http.put<Settings>(this.base() + '/notification-settings', body, {
            headers: {
              'Idempotency-Key': intent.key,
              'If-Match': intent.etag,
              'X-CSRF-TOKEN': csrf.token,
            },
            observe: 'response',
          }),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.settingsEtag = r.headers.get('ETag') ?? this.settingsEtag;
          this.message.set('Configuración guardada.');
        },
        error: (e) => this.failure(e),
      });
  }
  protected loadTemplates(cursor: string | null = null) {
    this.readPage<Template>(this.base() + '/notification-templates', cursor, (items, next) => {
      this.templates.set(items);
      this.templateCursor.set(next);
    });
  }
  protected loadLogs(cursor: string | null = null) {
    this.readPage<Log>(
      '/api/v1/admin/notifications',
      cursor,
      (items, next) => {
        this.logs.set(items);
        this.logCursor.set(next);
      },
      { applicationId: this.applicationId() },
    );
  }
  protected edit(value: Template) {
    this.template.setValue({
      eventType: value.eventType,
      locale: value.locale,
      subject: value.subject,
      body: value.body,
    });
    this.templateVersion = value.version;
    this.template.controls.eventType.disable();
    this.template.controls.locale.disable();
    this.intent = null;
  }
  protected newTemplate() {
    this.template.controls.eventType.enable();
    this.template.controls.locale.enable();
    this.template.reset({ eventType: 'Activated', locale: 'es', subject: '', body: '' });
    this.templateVersion = '0';
    this.intent = null;
  }
  protected saveTemplate() {
    if (this.busy() || this.template.invalid) return;
    const body = { ...this.template.getRawValue(), expectedVersion: this.templateVersion };
    const fingerprint = JSON.stringify(body);
    if (this.intent?.body !== fingerprint)
      this.intent = { body: fingerprint, key: crypto.randomUUID(), etag: '' };
    const key = this.intent.key;
    this.busy.set(true);
    this.http
      .get<{ token: string }>('/api/v1/admin/auth/csrf')
      .pipe(
        switchMap((csrf) =>
          this.http.post<Template>(this.base() + '/notification-templates', body, {
            headers: { 'Idempotency-Key': key, 'X-CSRF-TOKEN': csrf.token },
          }),
        ),
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (value) => {
          this.edit(value);
          this.message.set('Plantilla guardada.');
        },
        error: (e) => this.failure(e),
      });
  }
  private readPage<T>(
    url: string,
    cursor: string | null,
    apply: (items: T[], next: string | null) => void,
    extra: Record<string, string> = {},
  ) {
    if (this.busy()) return;
    this.busy.set(true);
    this.http
      .get<{ items: T[]; nextCursor: string | null }>(url, {
        params: { pageSize: 25, ...extra, ...(cursor ? { cursor } : {}) },
      })
      .pipe(
        timeout(10000),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (p) => apply(p.items, p.nextCursor), error: (e) => this.failure(e) });
  }
  private parse(text: string): number[] | null {
    if (!text.trim()) return [];
    const parts = text.split(',').map((x) => x.trim());
    return parts.every((x) => /^[0-9]+$/.test(x)) ? parts.map(Number) : null;
  }
  private base() {
    return '/api/v1/admin/applications/' + this.applicationId();
  }
  private failure(error: unknown) {
    const code = error instanceof HttpErrorResponse ? error.error?.code : '';
    this.message.set(
      code === 'precondition_failed'
        ? 'El registro cambió. Consúltalo de nuevo antes de guardar.'
        : code === 'not_found'
          ? 'Recurso inexistente o sin permiso.'
          : 'No se pudo completar la operación. Revisa canales, intervalos, variables y sesión.',
    );
  }
}
