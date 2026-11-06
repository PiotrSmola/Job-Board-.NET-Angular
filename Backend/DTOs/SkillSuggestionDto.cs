namespace Backend.DTOs;

public class SkillSuggestionDto
{
    public string Name { get; set; } = string.Empty;

    // oferty widoczne publicznie z tą umiejętnością
    public int JobsCount { get; set; }
}
