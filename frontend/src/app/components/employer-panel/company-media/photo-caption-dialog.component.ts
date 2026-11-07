import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { PHOTO_CAPTION_MAX } from '../../../services/employer-media.service';

export interface PhotoCaptionDialogData {
  caption: string | null;
}

/** Edycja podpisu zdjęcia; zamyka się nowym podpisem (pusty = null) albo undefined po anulowaniu. */
@Component({
  selector: 'app-photo-caption-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Opis zdjęcia</h2>
    <form (submit)="$event.preventDefault(); save()">
      <mat-dialog-content>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Opis</mat-label>
          <input matInput [formControl]="caption" [maxlength]="max" cdkFocusInitial aria-describedby="caption-hint">
          <mat-hint id="caption-hint">Widoczny pod zdjęciem i jako tekst alternatywny. Puste pole usuwa opis.</mat-hint>
          <mat-hint align="end">{{ caption.value.length }} / {{ max }}</mat-hint>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Anuluj</button>
        <button mat-flat-button type="submit" [disabled]="caption.invalid">Zapisz</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: [`mat-dialog-content { min-width: min(420px, 80vw); }`]
})
export class PhotoCaptionDialogComponent {
  private dialogRef = inject(MatDialogRef<PhotoCaptionDialogComponent, string | null>);
  private data = inject<PhotoCaptionDialogData>(MAT_DIALOG_DATA);

  readonly max = PHOTO_CAPTION_MAX;
  readonly caption = new FormControl(this.data.caption ?? '', {
    nonNullable: true,
    validators: [Validators.maxLength(PHOTO_CAPTION_MAX)]
  });

  save(): void {
    if (this.caption.invalid) return;
    // jak API: przycięcie i zwinięcie odstępów, pusty = brak podpisu
    const value = this.caption.value.trim().replace(/\s+/g, ' ');
    this.dialogRef.close(value || null);
  }
}
