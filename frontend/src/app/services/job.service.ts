import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { JobOffer, CreateJobOffer, JobOfferStatus } from '../models/job-offer.model';
import { PaginatedResult } from '../models/pagination.model';
import { JobParams, ManageJobParams } from '../models/job-params.model';
import { JobFacets } from '../models/taxonomy.model';
import { filterQuery } from '../utils/job-query';
import { toPaginatedResult } from '../utils/pagination';

@Injectable({
  providedIn: 'root'
})
export class JobService {
  private http = inject(HttpClient);
  private readonly API_URL = '/api/jobs';

  /** Publiczna lista – tylko oferty aktywne i niewygasłe. */
  getJobs(jobParams: JobParams): Observable<PaginatedResult<JobOffer>> {
    const params = this.buildParams(jobParams);

    return this.http.get<JobOffer[]>(this.API_URL, { params, observe: 'response' }).pipe(
      map(response => toPaginatedResult(response, jobParams.pageSize))
    );
  }

  /** Lista do zarządzania – admin: wszystkie oferty, pracodawca: oferty jego firmy; opcjonalny filtr stanu. */
  getManagedJobs(jobParams: ManageJobParams): Observable<PaginatedResult<JobOffer>> {
    let params = this.buildParams(jobParams);

    if (jobParams.state) {
      params = params.append('state', jobParams.state);
    }

    return this.http.get<JobOffer[]>(`${this.API_URL}/manage`, { params, observe: 'response' }).pipe(
      map(response => toPaginatedResult(response, jobParams.pageSize))
    );
  }

  getJob(id: number): Observable<JobOffer> {
    return this.http.get<JobOffer>(`${this.API_URL}/${id}`);
  }

  createJob(jobOffer: CreateJobOffer): Observable<JobOffer> {
    return this.http.post<JobOffer>(this.API_URL, jobOffer);
  }

  updateJob(id: number, jobOffer: CreateJobOffer): Observable<JobOffer> {
    return this.http.put<JobOffer>(`${this.API_URL}/${id}`, jobOffer);
  }

  /** Szybka zmiana stanu: publikacja, zamknięcie, wznowienie. */
  updateJobStatus(id: number, status: JobOfferStatus): Observable<JobOffer> {
    return this.http.patch<JobOffer>(`${this.API_URL}/${id}/status`, { status });
  }

  deleteJob(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/${id}`);
  }

  /** Liczniki opcji filtrów (kategorie, poziomy, umowy, tryby pracy, umiejętności) dla bieżących filtrów, bez paginacji. */
  getFacets(jobParams: JobParams): Observable<JobFacets> {
    const params = new HttpParams({ fromObject: filterQuery(jobParams) });
    return this.http.get<JobFacets>(`${this.API_URL}/facets`, { params });
  }

  private buildParams(jobParams: JobParams): HttpParams {
    return new HttpParams({
      fromObject: {
        pageNumber: jobParams.pageNumber.toString(),
        pageSize: jobParams.pageSize.toString(),
        ...filterQuery(jobParams),
        orderBy: jobParams.orderBy
      }
    });
  }
}
