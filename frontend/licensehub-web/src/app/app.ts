import { Component, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Subject, merge, of, startWith, switchMap, timer } from 'rxjs';
import { AvailabilityService } from './availability.service';
import { AdminLogin } from './admin-login';

@Component({
  imports: [
    AdminLogin,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatSlideToggleModule,
    MatToolbarModule,
  ],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly availabilityService = inject(AvailabilityService);
  private readonly refreshTrigger = new Subject<void>();
  protected readonly autoRefresh = new FormControl(false, { nonNullable: true });
  protected readonly darkMode = signal(localStorage.getItem('licensehub-theme') === 'dark');
  protected readonly availability = toSignal(
    merge(
      this.refreshTrigger,
      this.autoRefresh.valueChanges.pipe(
        startWith(false),
        switchMap((enabled) => (enabled ? timer(0, 30000) : of(0))),
      ),
    ).pipe(switchMap(() => this.availabilityService.check())),
    { initialValue: 'checking' },
  );
  constructor() {
    effect(() => {
      const theme = this.darkMode() ? 'dark' : 'light';
      document.documentElement.style.colorScheme = theme;
      localStorage.setItem('licensehub-theme', theme);
    });
  }
  protected refresh() {
    this.refreshTrigger.next();
  }
}
