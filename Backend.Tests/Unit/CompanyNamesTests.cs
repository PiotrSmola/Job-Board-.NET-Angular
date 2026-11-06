using Backend.Helpers;

namespace Backend.Tests.Unit;

public class CompanyNamesTests
{
    [Theory]
    [InlineData("Tech Corp", "Tech Corp")]
    [InlineData("  Tech   Corp  ", "Tech Corp")]
    [InlineData("Tech\tCorp\r\n", "Tech Corp")]
    [InlineData("Tech Corp", "Tech Corp")] // twarda spacja
    [InlineData("Tech 　Corp", "Tech Corp")] // em space, spacja ideograficzna
    [InlineData("Tech Corp​", "Tech Corp")] // zero-width space
    [InlineData("﻿Tech‌ Corp‍", "Tech Corp")] // BOM, ZWNJ, ZWJ
    [InlineData("Tech­Corp", "TechCorp")] // miękki dywiz
    [InlineData("Tech ​ Corp", "Tech Corp")]
    [InlineData("Tech\u0000\u0007Corp", "TechCorp")] // znaki kontrolne
    [InlineData("‮Tech Corp", "Tech Corp")] // zmiana kierunku tekstu
    [InlineData("Zażółć Gęślą", "Zażółć Gęślą")] // litery spoza ASCII zostają bez zmian
    public void Normalize_RemovesInvisibleCharactersAndCollapsesWhitespace(string input, string expected)
    {
        Assert.Equal(expected, CompanyNames.Normalize(input));
    }

    [Theory]
    [InlineData("​")]
    [InlineData("​ ﻿ ")]
    [InlineData("\u0007")]
    public void Normalize_InvisibleOnlyName_BecomesEmpty(string input)
    {
        Assert.Equal(string.Empty, CompanyNames.Normalize(input));
    }

    [Theory]
    [InlineData("​", false)]
    [InlineData("​­⁠", false)]
    [InlineData("A​", true)]
    [InlineData("Firma", true)]
    [InlineData("", true)] // pustą wartość odrzuca [Required]
    [InlineData(null, true)]
    public void CompanyNameAttribute_RequiresVisibleCharacters(string? value, bool expected)
    {
        Assert.Equal(expected, new CompanyNameAttribute().IsValid(value));
    }
}
