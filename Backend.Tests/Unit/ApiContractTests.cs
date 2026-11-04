using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Backend.Controllers;
using Backend.DTOs;
using Backend.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Tests.Unit;

// Kontrakt API: kontrolery zwracają DTO, a typy odpowiedzi nie niosą atrybutów walidacji wejścia
public class ApiContractTests
{
    private static readonly Assembly BackendAssembly = typeof(JobsController).Assembly;

    [Fact]
    public void JobOfferDto_ResponseType_HasNoValidationAttributes()
    {
        var attributes = typeof(JobOfferDto).GetProperties()
            .SelectMany(p => p.GetCustomAttributes<ValidationAttribute>().Select(a => $"{p.Name}: {a.GetType().Name}"));

        Assert.Empty(attributes);
    }

    [Fact]
    public void ControllerActions_NeverDeclareEntityTypesAsResponses()
    {
        var entityNamespace = typeof(Backend.Models.JobOffer).Namespace;
        var actions = BackendAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));

        var offending = actions
            .Where(m => ReferencedTypes(m.ReturnType).Any(t => t.Namespace == entityNamespace && !t.IsEnum))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.Empty(offending);
    }

    [Theory]
    [InlineData(typeof(IJobRepository))]
    [InlineData(typeof(IJobApplicationRepository))]
    public void RepositoryInterfaces_DoNotExposeSaveAll(Type repositoryInterface)
    {
        Assert.Null(repositoryInterface.GetMethod("SaveAllAsync"));
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments().SelectMany(ReferencedTypes))
        {
            yield return argument;
        }
    }
}
