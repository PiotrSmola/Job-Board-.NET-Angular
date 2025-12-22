import { Component, inject, OnInit, ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { JobService } from '../../services/job.service';
import { JobApplicationService } from '../../services/job-application.service';
import { AuthService } from '../../services/auth.service';
import { JobOffer, WorkType } from '../../models/job-offer.model';

@Component({
  selector: 'app-apply-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule
  ],
  template: `
    <h2 mat-dialog-title>Aplikuj na stanowisko</h2>
    <mat-dialog-content>
      <p>Dodaj notatkę do swojej aplikacji (opcjonalnie):</p>
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Notatka</mat-label>
        <textarea matInput [(ngModel)]="note" rows="4" placeholder="Dlaczego jesteś idealnym kandydatem..."></textarea>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Anuluj</button>
      <button mat-raised-button color="primary" [mat-dialog-close]="note">Aplikuj</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full-width { width: 100%; }
    mat-dialog-content { min-width: 400px; padding: 20px; }
  `]
})
export class ApplyDialogComponent {
  note = '';
}

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatDividerModule,
    MatDialogModule,
    RouterModule
  ],
  templateUrl: './job-detail.component.html',
  styleUrl: './job-detail.component.scss'
})
export class JobDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private jobService = inject(JobService);
  private applicationService = inject(JobApplicationService);
  authService = inject(AuthService);
  private dialog = inject(MatDialog);
  private cdr = inject(ChangeDetectorRef);

  job: JobOffer | null = null;
  loading = false;
  hasApplied = false;
  checkingApplication = false;
  applying = false;

  WorkType = WorkType;

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const id = params.get('id');
      if (id) {
        this.loadJob(+id);
        if (this.authService.isAuthenticated()) {
          this.checkIfApplied(+id);
        }
      }
    });
  }

  loadJob(id: number): void {
    this.loading = true;
    this.jobService.getJob(id).subscribe({
      next: (job) => {
        this.job = job;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading job:', err);
        this.loading = false;
        this.cdr.detectChanges();
        // Don't redirect immediately, maybe show error in UI
      }
    });
  }

  checkIfApplied(jobId: number): void {
    this.checkingApplication = true;
    this.applicationService.checkIfApplied(jobId).subscribe({
      next: (result) => {
        this.hasApplied = result.hasApplied;
        this.checkingApplication = false;
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error checking application status:', error);
        this.checkingApplication = false;
        this.hasApplied = false; // Reset to allow applying if check fails
        this.cdr.detectChanges();
      }
    });
  }

  openApplyDialog(): void {
    if (!this.job) return;

    const dialogRef = this.dialog.open(ApplyDialogComponent);

    dialogRef.afterClosed().subscribe((note: string | undefined) => {
      if (note !== undefined && this.job) {
        this.applyToJob(note);
      }
    });
  }

  applyToJob(note: string): void {
    if (!this.job) return;

    this.applying = true;
    this.applicationService.applyToJob({
      jobOfferId: this.job.id,
      note: note || null
    }).subscribe({
      next: () => {
        this.hasApplied = true;
        this.applying = false;
        alert('Aplikacja wysłana pomyślnie!');
      },
      error: (error) => {
        this.applying = false;
        alert(error.error?.message || 'Błąd podczas aplikowania');
      }
    });
  }

  getWorkTypeLabel(): string {
    if (!this.job) return '';
    switch (this.job.workType) {
      case WorkType.Remote: return 'Zdalna';
      case WorkType.Hybrid: return 'Hybrydowa';
      case WorkType.Onsite: return 'Stacjonarna';
      default: return 'Nieznany';
    }
  }

  formatSalary(): string {
    if (!this.job) return '';
    const { salaryMin, salaryMax } = this.job;
    if (!salaryMin && !salaryMax) return 'Nieokreślona';
    if (salaryMin && salaryMax) return `${salaryMin} - ${salaryMax} PLN`;
    if (salaryMin) return `Od ${salaryMin} PLN`;
    if (salaryMax) return `Do ${salaryMax} PLN`;
    return 'Nieokreślona';
  }

  goBack(): void {
    this.router.navigate(['/jobs']);
  }
}
