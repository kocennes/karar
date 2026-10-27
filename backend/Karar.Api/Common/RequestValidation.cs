using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Karar.Api.Common;

/// <summary>
/// DataAnnotations validation for request DTOs, including positional records.
/// </summary>
/// <remarks>
/// In <c>record R([Required] string X)</c> the attribute is emitted on the constructor parameter, not on the
/// generated property, so <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}?, bool)"/>
/// never sees it. Every request contract in Contracts/Requests.cs is written that way, which left all of their
/// [Required]/[StringLength]/[Range]/[EmailAddress]/[RegularExpression] rules unenforced (null fields reached
/// handlers as NullReferenceException/ArgumentNullException 500s, CHECK-constraint violations became 500s).
/// This validates the property-level attributes as before, plus the attributes declared on public constructor
/// parameters, against the value of the same-named property.
/// </remarks>
public static class RequestValidation
{
    private static readonly ConcurrentDictionary<Type, ParameterRule[]> Rules = new();

    public static bool TryValidate(object instance, ICollection<ValidationResult> results)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var valid = Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);

        foreach (var rule in Rules.GetOrAdd(instance.GetType(), BuildRules))
        {
            var context = new ValidationContext(instance)
            {
                MemberName = rule.Property.Name,
                DisplayName = rule.Property.Name,
            };
            valid &= Validator.TryValidateValue(rule.Property.GetValue(instance), context, results, rule.Attributes);
        }

        return valid;
    }

    private static ParameterRule[] BuildRules(Type type)
    {
        var rules = new Dictionary<string, ParameterRule>(StringComparer.Ordinal);

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (parameter.Name is null || rules.ContainsKey(parameter.Name)) continue;

                var property = type.GetProperty(parameter.Name, BindingFlags.Public | BindingFlags.Instance);
                if (property is null || !property.CanRead || property.GetIndexParameters().Length > 0) continue;

                // Attributes already on the property are covered by TryValidateObject; do not report them twice.
                var onProperty = property.GetCustomAttributes<ValidationAttribute>(inherit: true)
                    .Select(a => a.GetType())
                    .ToHashSet();
                var attributes = parameter.GetCustomAttributes<ValidationAttribute>(inherit: true)
                    .Where(a => !onProperty.Contains(a.GetType()))
                    .ToArray();

                if (attributes.Length > 0)
                {
                    rules[parameter.Name] = new ParameterRule(property, attributes);
                }
            }
        }

        return rules.Values.ToArray();
    }

    private sealed record ParameterRule(PropertyInfo Property, ValidationAttribute[] Attributes);
}
