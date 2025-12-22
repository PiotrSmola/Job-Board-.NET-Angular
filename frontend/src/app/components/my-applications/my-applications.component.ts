import { Component, inject, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { JobApplicationService } from '../../services/job-application.service';
import { JobApplication } from '../../models/job-application.model';

@Component({
  selector: 'app-my-applications',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatProgressSpinnerModule,
    MatTooltipModule
  ],
  templateUrl: './my-applications.component.html',
  styleUrl: './my-applications.component.scss'
})
export class MyApplicationsComponent implements OnInit {
  private applicationService = inject(JobApplicationService);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);

  applications: JobApplication[] = [];
  displayedColumns: string[] = ['jobTitle', 'company', 'location', 'appliedAt', 'note', 'actions'];
  loading = false;
  deleting = false;
  deletingId: number | null = null;

  ngOnInit(): void {
    this.loadApplications();
  }

  loadApplications(): void {
    this.loading = true;
    this.applicationService.getMyApplications().subscribe({
      next: (applications) => {
        this.applications = applications;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (error) => {
        console.error('Error loading applications:', error);
        this.loading = false;
        this.cdr.detectChanges();
        alert('Błąd podczas ładowania aplikacji');
      }
    });
  }

  deleteApplication(id: number): void {
    if (!confirm('Czy na pewno chcesz usunąć tę aplikację?')) {
      return;
    }

    this.deleting = true;
    this.deletingId = id;
    this.applicationService.deleteApplication(id).subscribe({
      next: () => {
        this.applications = this.applications.filter(app => app.id !== id);
        this.deleting = false;
        this.deletingId = null;
        this.cdr.detectChanges();
        alert('Aplikacja została usunięta');
      },
      error: (error) => {
        console.error('Error deleting application:', error);
        this.deleting = false;
        this.deletingId = null;
        this.cdr.detectChanges();
        alert('Błąd podczas usuwania aplikacji');
      }
    });
  }

  viewJobDetails(jobId: number): void {
    this.router.navigate(['/jobs', jobId]);
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('pl-PL', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }
}
