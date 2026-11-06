using System.IO.Compression;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// Pomocnicze elementy samych testów: powtarzalne pliki testowe i kategorie testów
public class TestInfrastructureTests
{
    // Oczekiwana treść pobranego załącznika jest budowana ponownie (np. ThreeFiles()) - DOCX musi mieć te same bajty
    // niezależnie od chwili zbudowania (czas wpisu ZIP stały, nie bieżący)
    [Fact]
    public void Docx_HasFixedEntryTimes_SoRebuildingItGivesTheSameBytes()
    {
        var docx = AttachmentTestFiles.Docx();

        using (var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            Assert.NotEmpty(archive.Entries);
            Assert.All(archive.Entries, e => Assert.Equal(AttachmentTestFiles.ZipEntryTime.DateTime, e.LastWriteTime.DateTime));
        }

        Assert.Equal(docx, AttachmentTestFiles.Docx());
    }

    // Szybki zestaw (dotnet test --filter "Category!=Migrations") pomija testy migracji - każda ich klasa ma kategorię
    [Fact]
    public void EveryMigrationTestClass_HasTheMigrationsCategory()
    {
        var migrationClasses = typeof(TestInfrastructureTests).Assembly.GetTypes()
            .Where(t => t.IsClass && t.Name.EndsWith("MigrationTests", StringComparison.Ordinal))
            .ToList();

        Assert.True(migrationClasses.Count >= 4, $"Expected the migration test classes, found {migrationClasses.Count}");
        Assert.All(migrationClasses, type => Assert.Contains(type.CustomAttributes,
            a => a.AttributeType == typeof(TraitAttribute)
                 && a.ConstructorArguments.Select(arg => arg.Value as string).SequenceEqual(new[] { "Category", "Migrations" })));
    }
}
