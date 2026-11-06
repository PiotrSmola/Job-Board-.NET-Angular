import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEventType, HttpParams } from '@angular/common/http';
import { Observable, filter, map } from 'rxjs';
import {
  ApplicationCheck,
  CreateJobApplication,
  CvUploadEvent,
  JobApplication,
  UpdateApplicationStatus
} from '../models/job-application.model';
import { ApplicationParams } from '../models/application-params.model';
import { PaginatedResult } from '../models/pagination.model';
import { toPaginatedResult } from '../utils/pagination';
import { resolveDownloadFileName } from '../utils/content-disposition';
import { saveBlobAsFile } from '../utils/file-download';

@Injectable({
  providedIn: 'root'
})
export class JobApplicationService {
  private http = inject(HttpClient);
  private readonly API_URL = '/api/jobapplications';

  getMyApplications(): Observable<JobApplication[]> {
    return this.http.get<JobApplication[]>(`${this.API_URL}/my-applications`);
  }

  /** Szczegóły aplikacji z historią statusów (właściciel lub admin). */
  getApplication(id: number): Observable<JobApplication> {
    return this.http.get<JobApplication>(`${this.API_URL}/${id}`);
  }

  /** Złożenie aplikacji – także ponowne po wycofaniu (API reaktywuje tę samą aplikację). */
  applyToJob(application: CreateJobApplication): Observable<JobApplication> {
    return this.http.post<JobApplication>(this.API_URL, application);
  }

  checkIfApplied(jobId: number): Observable<ApplicationCheck> {
    return this.http.get<ApplicationCheck>(`${this.API_URL}/check/${jobId}`);
  }

  /** Zmiana statusu przez rekrutera (admin); zwraca aplikację z historią. */
  updateStatus(id: number, body: UpdateApplicationStatus): Observable<JobApplication> {
    return this.http.put<JobApplication>(`${this.API_URL}/${id}/status`, body);
  }

  /** Wycofanie aplikacji przez kandydata; zwraca aplikację z historią. */
  withdraw(id: number): Observable<JobApplication> {
    return this.http.post<JobApplication>(`${this.API_URL}/${id}/withdraw`, null);
  }

  /** Trwałe usunięcie – tylko admin. */
  deleteApplication(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/${id}`);
  }

  /**
   * Dołączenie lub zastąpienie CV (właściciel, statusy jak przy wycofaniu).
   * Emituje postęp wysyłania, a na końcu zaktualizowaną aplikację.
   */
  uploadCv(id: number, file: File): Observable<CvUploadEvent> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    return this.http.put<JobApplication>(`${this.API_URL}/${id}/cv`, formData, {
      observe: 'events',
      reportProgress: true
    }).pipe(
      map((event): CvUploadEvent | null => {
        if (event.type === HttpEventType.UploadProgress) {
          const percent = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
          return { type: 'progress', percent: Math.min(percent, 100) };
        }
        if (event.type === HttpEventType.Response && event.body) {
          return { type: 'done', application: event.body };
        }
        return null;
      }),
      filter((event): event is CvUploadEvent => event !== null)
    );
  }

  /** Usunięcie CV z aplikacji (właściciel). */
  deleteCv(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/${id}/cv`);
  }

  /**
   * Pobiera CV (właściciel, admin, pracodawca firmy oferty) i zapisuje je na dysku.
   * Nazwa pliku z nagłówka Content-Disposition, w drugiej kolejności `fallbackFileName` (np. `cvFileName` z DTO).
   */
  downloadCv(id: number, fallbackFileName: string | null = null): Observable<void> {
    return this.http.get(`${this.API_URL}/${id}/cv`, { observe: 'response', responseType: 'blob' }).pipe(
      map(response => {
        const fileName = resolveDownloadFileName(response.headers.get('Content-Disposition'), fallbackFileName);
        saveBlobAsFile(response.body ?? new Blob(), fileName);
      })
    );
  }

  getAllApplications(applicationParams: ApplicationParams): Observable<PaginatedResult<JobApplication>> {
    let params = new HttpParams()
      .append('pageNumber', applicationParams.pageNumber.toString())
      .append('pageSize', applicationParams.pageSize.toString())
      .append('orderBy', applicationParams.orderBy);

    if (applicationParams.searchTerm) {
      params = params.append('searchTerm', applicationParams.searchTerm);
    }

    if (applicationParams.jobOfferId !== null) {
      params = params.append('jobOfferId', applicationParams.jobOfferId.toString());
    }

    if (applicationParams.status !== null) {
      params = params.append('status', applicationParams.status.toString());
    }

    return this.http.get<JobApplication[]>(`${this.API_URL}/all`, { params, observe: 'response' }).pipe(
      map(response => toPaginatedResult(response, applicationParams.pageSize))
    );
  }
}
