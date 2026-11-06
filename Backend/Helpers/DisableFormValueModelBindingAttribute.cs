using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backend.Helpers;

// Wyłącza dostawców wartości z formularza dla akcji, która czyta formularz sama (upload załączników).
// Bez tego MVC przy wiązaniu parametrów (np. id z trasy) wczytałby cały formularz multipart z domyślnymi limitami -
// zanim akcja sprawdzi właściciela aplikacji i zanim ustawi własne limity.
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
