import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { MyApplicationsComponent } from './my-applications.component';
import { JobApplicationService } from '../../services/job-application.service';
import { JobApplication } from '../../models/job-application.model';

function makeApplication(id: number): JobApplication {
  return {
    id, jobOfferId: id * 10, userId: 'u1', userEmail: 'jan@example.com', userFullName: 'Jan Kowalski',
    jobTitle: `Oferta ${id}`, company: 'Acme', note: null, appliedAt: new Date('2026-01-01T10:00:00Z')
  };
}

describe('MyApplicationsComponent', () => {
  let fixture: ComponentFixture<MyApplicationsComponent>;
  let component: MyApplicationsComponent;
  let getMyApplications: ReturnType<typeof vi.fn>;
  let deleteApplication: ReturnType<typeof vi.fn>;
  let alertSpy: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;

  function create(initial: Observable<JobApplication[]> = of([makeApplication(1), makeApplication(2)])): void {
    getMyApplications.mockReturnValue(initial);
    fixture = TestBed.createComponent(MyApplicationsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    getMyApplications = vi.fn();
    deleteApplication = vi.fn().mockReturnValue(of(undefined));
    alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [MyApplicationsComponent],
      providers: [provideRouter([]), { provide: JobApplicationService, useValue: { getMyApplications, deleteApplication } }]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('loads and shows the current user applications', () => {
    create();

    expect(component.applications.map(a => a.id)).toEqual([1, 2]);
    expect(component.loading).toBe(false);
  });

  it('alerts when loading fails', () => {
    create(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(alertSpy).toHaveBeenCalledWith('Błąd podczas ładowania aplikacji');
    expect(component.loading).toBe(false);
  });

  it('removes the deleted application from the list after confirmation', () => {
    create();

    component.deleteApplication(1);

    expect(deleteApplication).toHaveBeenCalledWith(1);
    expect(component.applications.map(a => a.id)).toEqual([2]);
    expect(component.deletingId).toBeNull();
    expect(alertSpy).toHaveBeenCalledWith('Aplikacja została usunięta');
  });

  it('does not delete when the confirmation is declined', () => {
    create();
    confirmSpy.mockReturnValue(false);

    component.deleteApplication(1);

    expect(deleteApplication).not.toHaveBeenCalled();
    expect(component.applications.length).toBe(2);
  });

  it('keeps the list and alerts when deletion fails', () => {
    create();
    deleteApplication.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    component.deleteApplication(1);

    expect(component.applications.length).toBe(2);
    expect(component.deleting).toBe(false);
    expect(alertSpy).toHaveBeenCalledWith('Błąd podczas usuwania aplikacji');
  });

  it('viewJobDetails navigates to the job page', () => {
    create();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.viewJobDetails(20);

    expect(navigate).toHaveBeenCalledWith(['/jobs', 20]);
  });
});
