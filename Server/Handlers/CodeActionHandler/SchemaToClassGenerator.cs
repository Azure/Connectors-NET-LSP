using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SdkLspServer.Handlers.CodeActionHandler;

/// <summary>
/// Generates C# classes from JSON Schema definitions returned by dynamic schema discovery endpoints.
/// </summary>
internal static class SchemaToClassGenerator
{
    /// <summary>
    /// Generates a C# class that derives from a dynamic schema base type, with typed properties
    /// derived from the JSON Schema.
    /// </summary>
    /// <param name="className">The name for the generated class (e.g., "PostMessageInput").</param>
    /// <param name="baseClassName">The base class to inherit from (e.g., "DynamicPostMessageRequest").</param>
    /// <param name="baseClassNamespace">The namespace of the base class.</param>
    /// <param name="schema">The JSON Schema describing the properties.</param>
    /// <param name="targetNamespace">The namespace for the generated class.</param>
    public static string GenerateClass(
        string className,
        string baseClassName,
        string baseClassNamespace,
        JsonElement schema,
        string targetNamespace)
    {
        var nestedClasses = new List<string>();
        var builder = new StringBuilder();

        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Text.Json;");
        builder.AppendLine("using System.Text.Json.Serialization;");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(baseClassNamespace) &&
            !string.Equals(baseClassNamespace, targetNamespace, StringComparison.Ordinal))
        {
            builder.AppendLine($"using {baseClassNamespace};");
            builder.AppendLine();
        }

        builder.AppendLine($"namespace {targetNamespace};");
        builder.AppendLine();

        GenerateClassBody(builder, className, baseClassName, schema, nestedClasses, indentLevel: 0);

        foreach (string nested in nestedClasses)
        {
            builder.AppendLine();
            builder.Append(nested);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Generates a standalone C# class body (without file-level scaffolding) for a JSON Schema object.
    /// Used both for the root class and for nested object types.
    /// </summary>
    internal static void GenerateClassBody(
        StringBuilder builder,
        string className,
        string? baseClassName,
        JsonElement schema,
        List<string> nestedClasses,
        int indentLevel)
    {
        string indent = new(' ', indentLevel * 4);
        string memberIndent = new(' ', (indentLevel + 1) * 4);

        if (TryGetDescription(schema, out string? classDescription) && classDescription != null)
        {
            builder.AppendLine($"{indent}/// <summary>");
            builder.AppendLine($"{indent}/// {EscapeXmlDoc(classDescription)}");
            builder.AppendLine($"{indent}/// </summary>");
        }

        string inheritance = string.IsNullOrEmpty(baseClassName) ? string.Empty : $" : {baseClassName}";
        builder.AppendLine($"{indent}public class {className}{inheritance}");
        builder.AppendLine($"{indent}{{");

        if (schema.TryGetProperty("properties", out JsonElement properties))
        {
            bool firstProperty = true;
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (!firstProperty)
                {
                    builder.AppendLine();
                }

                firstProperty = false;

                string jsonName = property.Name;
                string csharpName = ToPascalCase(jsonName);
                string csharpType = ResolveCSharpType(property.Value, csharpName, nestedClasses);

                if (TryGetDescription(property.Value, out string? propDescription) && propDescription != null)
                {
                    builder.AppendLine($"{memberIndent}/// <summary>");
                    builder.AppendLine($"{memberIndent}/// {EscapeXmlDoc(propDescription)}");
                    builder.AppendLine($"{memberIndent}/// </summary>");
                }

                builder.AppendLine($"{memberIndent}[JsonPropertyName(\"{jsonName}\")]");
                builder.AppendLine($"{memberIndent}public {csharpType} {csharpName} {{ get; set; }}");
            }
        }

        builder.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// Maps a JSON Schema type to a C# type string. For nested objects with properties,
    /// generates a new class and returns its name.
    /// </summary>
    internal static string ResolveCSharpType(JsonElement schema, string propertyName, List<string> nestedClasses)
    {
        string? schemaType = null;
        if (schema.TryGetProperty("type", out JsonElement typeElement))
        {
            schemaType = typeElement.GetString();
        }

        if (string.IsNullOrEmpty(schemaType))
        {
            return "JsonElement?";
        }

        return schemaType switch
        {
            "string" => GetStringType(schema),
            "integer" => "int?",
            "number" => "double?",
            "boolean" => "bool?",
            "object" => ResolveObjectType(schema, propertyName, nestedClasses),
            "array" => ResolveArrayType(schema, propertyName, nestedClasses),
            _ => "JsonElement?",
        };
    }

    private static string GetStringType(JsonElement schema)
    {
        if (schema.TryGetProperty("format", out JsonElement format))
        {
            string? formatValue = format.GetString();
            if (string.Equals(formatValue, "date-time", StringComparison.OrdinalIgnoreCase))
            {
                return "DateTimeOffset?";
            }

            if (string.Equals(formatValue, "uri", StringComparison.OrdinalIgnoreCase))
            {
                return "string?";
            }
        }

        return "string?";
    }

    private static string ResolveObjectType(JsonElement schema, string propertyName, List<string> nestedClasses)
    {
        if (!schema.TryGetProperty("properties", out _))
        {
            return "JsonElement?";
        }

        string nestedClassName = propertyName;
        var nestedBuilder = new StringBuilder();
        GenerateClassBody(nestedBuilder, nestedClassName, baseClassName: null, schema, nestedClasses, indentLevel: 0);
        nestedClasses.Add(nestedBuilder.ToString());

        return $"{nestedClassName}?";
    }

    private static string ResolveArrayType(JsonElement schema, string propertyName, List<string> nestedClasses)
    {
        if (!schema.TryGetProperty("items", out JsonElement items))
        {
            return "List<JsonElement>?";
        }

        string itemType = ResolveCSharpType(items, propertyName + "Item", nestedClasses);

        // Remove nullable suffix for the list item type (List<T?> is redundant)
        if (itemType.EndsWith("?", StringComparison.Ordinal))
        {
            itemType = itemType[..^1];
        }

        return $"List<{itemType}>?";
    }

    /// <summary>
    /// Converts a camelCase or snake_case JSON property name to PascalCase.
    /// </summary>
    internal static string ToPascalCase(string jsonName)
    {
        if (string.IsNullOrEmpty(jsonName))
        {
            return jsonName;
        }

        var builder = new StringBuilder(jsonName.Length);
        bool capitalizeNext = true;

        foreach (char c in jsonName)
        {
            if (c == '_' || c == '-')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                builder.Append(char.ToUpper(c, CultureInfo.InvariantCulture));
                capitalizeNext = false;
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool TryGetDescription(JsonElement schema, out string? description)
    {
        description = null;
        if (schema.TryGetProperty("description", out JsonElement descElement) &&
            descElement.ValueKind == JsonValueKind.String)
        {
            description = descElement.GetString();
            return !string.IsNullOrWhiteSpace(description);
        }

        if (schema.TryGetProperty("x-ms-summary", out JsonElement summaryElement) &&
            summaryElement.ValueKind == JsonValueKind.String)
        {
            description = summaryElement.GetString();
            return !string.IsNullOrWhiteSpace(description);
        }

        return false;
    }

    private static string EscapeXmlDoc(string text)
    {
        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
