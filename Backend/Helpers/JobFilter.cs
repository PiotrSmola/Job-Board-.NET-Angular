using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Backend.Models;

namespace Backend.Helpers;

// Filtry listy ofert po sparsowaniu i walidacji parametrów zapytania (JobParams); wszystkie opcjonalne, łączone AND
public sealed record JobFilter
{
    public string? SearchTerm { get; init; }
    public int? CompanyId { get; init; }
    public string? Location { get; init; }
    public decimal? MinSalary { get; init; }
    public decimal? MaxSalary { get; init; }

    // slug kategorii (małe litery) i/lub jej id
    public string? CategorySlug { get; init; }
    public int? CategoryId { get; init; }

    // listy = OR w obrębie filtra; pusta lista = brak filtra
    public IReadOnlyList<WorkType> WorkTypes { get; init; } = [];
    public IReadOnlyList<SeniorityLevel> Seniority { get; init; } = [];

    // maska bitowa: oferta pasuje, gdy ma którykolwiek z zaznaczonych typów; None = brak filtra
    public ContractTypes ContractTypes { get; init; }

    // klucze umiejętności (SkillNames.KeyOf): oferta musi mieć WSZYSTKIE
    public IReadOnlyList<string> SkillKeys { get; init; } = [];

    public Currency? Currency { get; init; }
}

// Parsowanie list rozdzielanych przecinkami z parametrów zapytania (workType=0,1; seniority=1,2; skills=A,B)
public static class JobFilterParser
{
    public const int MaxSkills = 10;

    // Zwraca filtr albo (przy niepoprawnych wartościach) listę błędów walidacji
    public static (JobFilter? Filter, IReadOnlyList<ValidationResult> Errors) Parse(JobParams p)
    {
        var errors = new List<ValidationResult>();

        var workTypes = ParseEnums<WorkType>(p.WorkType, nameof(JobParams.WorkType), errors);
        var seniority = ParseEnums<SeniorityLevel>(p.Seniority, nameof(JobParams.Seniority), errors);
        var skillKeys = ParseSkills(p.Skills, errors);

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new JobFilter
        {
            SearchTerm = string.IsNullOrWhiteSpace(p.SearchTerm) ? null : p.SearchTerm,
            CompanyId = p.CompanyId,
            Location = string.IsNullOrWhiteSpace(p.Location) ? null : p.Location,
            MinSalary = p.MinSalary,
            MaxSalary = p.MaxSalary,
            CategorySlug = string.IsNullOrWhiteSpace(p.Category) ? null : p.Category.ToLowerInvariant(),
            CategoryId = p.CategoryId,
            WorkTypes = workTypes,
            Seniority = seniority,
            ContractTypes = (ContractTypes)(p.ContractTypes ?? 0),
            SkillKeys = skillKeys,
            Currency = p.Currency
        }, errors);
    }

    // Elementy: liczba albo nazwa wartości enuma (bez wielkości liter); puste elementy są pomijane, duplikaty scalane
    private static List<T> ParseEnums<T>(string? raw, string parameter, List<ValidationResult> errors) where T : struct, Enum
    {
        var values = new List<T>();
        foreach (var token in Split(raw))
        {
            if (!TryParseEnum<T>(token, out var value))
            {
                errors.Add(new ValidationResult($"'{token}' is not a valid value of {parameter}", [parameter]));
                return [];
            }

            if (!values.Contains(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static bool TryParseEnum<T>(string token, out T value) where T : struct, Enum
    {
        value = default;
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            if (!Enum.IsDefined(typeof(T), number))
            {
                return false;
            }

            value = (T)Enum.ToObject(typeof(T), number);
            return true;
        }

        // nazwa: wyłącznie litery (Enum.TryParse przyjmuje też listy "A, B" i liczby ze znakiem)
        return token.All(char.IsLetter) && Enum.TryParse(token, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    private static List<string> ParseSkills(string? raw, List<ValidationResult> errors)
    {
        var keys = new List<string>();
        var count = 0;
        foreach (var token in Split(raw))
        {
            if (++count > MaxSkills)
            {
                errors.Add(new ValidationResult($"At most {MaxSkills} skills can be used as a filter", [nameof(JobParams.Skills)]));
                return [];
            }

            var name = SkillNames.Normalize(token);
            if (!SkillNames.IsValid(name))
            {
                errors.Add(new ValidationResult(
                    $"Each skill must have 1-{SkillNames.MaxLength} characters", [nameof(JobParams.Skills)]));
                return [];
            }

            var key = SkillNames.KeyOf(name);
            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static IEnumerable<string> Split(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(SkillNames.ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
