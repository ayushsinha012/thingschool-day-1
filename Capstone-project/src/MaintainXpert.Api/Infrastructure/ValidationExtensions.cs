using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace MaintainXpert.Api.Infrastructure;

public static class ValidationExtensions
{
    public static IResult? Validate<T>(T value) where T : notnull
    {
        var type = typeof(T);

        var primaryConstructor = type.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .FirstOrDefault();

        var errors = new Dictionary<string, List<string>>();

        foreach (var property in type.GetProperties())
        {
            var attributes = property
                .GetCustomAttributes<ValidationAttribute>(inherit: true)
                .ToList();

            if (attributes.Count == 0)
            {
                var matchingParameter = primaryConstructor?
                    .GetParameters()
                    .FirstOrDefault(parameter =>
                        string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase));

                if (matchingParameter is not null)
                {
                    attributes = matchingParameter
                        .GetCustomAttributes<ValidationAttribute>(inherit: true)
                        .ToList();
                }
            }

            if (attributes.Count == 0)
            {
                continue;
            }

            var propertyValue = property.GetValue(value);

            foreach (var attribute in attributes)
            {
                if (attribute.IsValid(propertyValue))
                {
                    continue;
                }

                if (!errors.TryGetValue(property.Name, out var messages))
                {
                    messages = [];
                    errors[property.Name] = messages;
                }

                messages.Add(attribute.FormatErrorMessage(property.Name));
            }
        }

        if (errors.Count == 0)
        {
            return null;
        }

        return Results.ValidationProblem(
            errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()));
    }
}
