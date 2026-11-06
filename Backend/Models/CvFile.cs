namespace Backend.Models;

// Dane CV zapisywane w aplikacji (kolumny Cv* w JobApplications); Key - klucz pliku w magazynie
public sealed record CvFile(string Key, string FileName, string ContentType, long Size, DateTime UploadedAt);
