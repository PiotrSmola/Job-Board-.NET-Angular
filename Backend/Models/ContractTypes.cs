namespace Backend.Models;

// Maska bitowa: oferta może przewidywać kilka typów umowy naraz (0 = nie podano)
[Flags]
public enum ContractTypes
{
    None = 0,
    EmploymentContract = 1,
    B2B = 2,
    MandateContract = 4,
    SpecificTaskContract = 8,
    Internship = 16
}

public static class ContractTypesExtensions
{
    // suma wszystkich bitów (31) - wartość spoza tej maski jest niepoprawna
    public const int AllFlags = 1 | 2 | 4 | 8 | 16;

    // pojedyncze bity (do liczników faset)
    public static readonly IReadOnlyList<ContractTypes> SingleFlags = Enum.GetValues<ContractTypes>()
        .Where(v => v != ContractTypes.None)
        .ToArray();

    public static bool IsValidMask(int value) => value >= 0 && (value & ~AllFlags) == 0;
}
