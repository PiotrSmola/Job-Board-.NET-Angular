import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import {
  ApplicationCheck,
  CreateJobApplication,
  JobApplication,
  UpdateApplicationStatus
} from '../models/job-application.model';
import { ApplicationParams } from '../models/application-params.model';
import { PaginatedResult } from '../models/pagination.model';
import { toPaginatedResult } from '../utils/pagination';

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
