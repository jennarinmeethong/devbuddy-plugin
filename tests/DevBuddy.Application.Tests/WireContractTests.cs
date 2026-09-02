using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DevBuddy.Application.Dispatch;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Every operation argument and result type can actually be built from JSON.
/// <para>
/// This exists because of a real bug, and the bug is worth describing because the shape of it
/// recurs. <c>Provenance</c> took its evidence as <c>IEnumerable&lt;T&gt;</c> and exposed it as
/// <c>IReadOnlyList&lt;T&gt;</c>. Nothing about that is wrong for any caller in the codebase, and
/// the serialiser writes it happily. It cannot read it back: construction by name needs the
/// parameter and the property it names to have the same type, so the parameter went unbound and
/// every attempt to deserialise the whole type threw. It surfaced as a 500 from
/// <c>create_draft</c> — one host, one operation, at run time.
/// </para>
/// <para>
/// The check asks the serialiser itself rather than re-deriving its rules by reflection, because
/// the rules are the serialiser's and they change between versions. If it cannot bind a
/// parameter, that is the failure, whatever the reason turns out to be.
/// </para>
/// </summary>
public sealed class WireContractTests
{
    [Fact]
    public void every_operation_argument_type_can_be_constructed_from_json()
    {
        List<string> broken = [];
        HashSet<Type> seen = [];

        foreach (Type type in UseCaseTypes().Select(type => BaseArguments(type)[0]))
        {
            Check(type, seen, broken);
        }

        Assert.Empty(broken);
    }

    /// <summary>
    /// And the same for what goes back out. A response that cannot be read back is a contract no
    /// generated client can hold up its end of, even though the server serialises it happily.
    /// </summary>
    [Fact]
    public void every_operation_result_type_can_be_constructed_from_json()
    {
        List<string> broken = [];
        HashSet<Type> seen = [];

        foreach (Type type in UseCaseTypes().Select(type => BaseArguments(type)[1]))
        {
            Check(type, seen, broken);
        }

        Assert.Empty(broken);
    }

    /// <summary>
    /// Asks the wire options for the type's metadata and checks that every constructor parameter
    /// found a property, then walks the property types.
    /// </summary>
    private static void Check(Type type, HashSet<Type> seen, List<string> broken)
    {
        if (!IsOurs(type) || !seen.Add(type))
        {
            return;
        }

        JsonTypeInfo info;

        try
        {
            info = JsonConventions.Options.GetTypeInfo(type);
        }
        catch (InvalidOperationException failure)
        {
            broken.Add($"{type.Name} has no usable JSON contract: {failure.Message}");
            return;
        }

        // A type the options convert wholesale — an identifier struct, a scope — has no
        // properties to check, and its converter is what the round-trip tests cover.
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        if (DeserializationConstructor(type) is { } constructor)
        {
            foreach (string parameter in constructor.GetParameters().Select(p => p.Name!))
            {
                bool bound = info.Properties.Any(property =>
                    string.Equals(property.AssociatedParameter?.Name, parameter, StringComparison.Ordinal));

                if (!bound)
                {
                    broken.Add(
                        $"{type.Name}.{parameter} is a constructor parameter no property binds to, "
                        + "so this type cannot be deserialised at all.");
                }
            }
        }

        foreach (JsonPropertyInfo property in info.Properties)
        {
            Check(Unwrap(property.PropertyType), seen, broken);
        }
    }

    /// <summary>
    /// The constructor System.Text.Json would use: one marked <c>[JsonConstructor]</c>, or the
    /// single public one that takes parameters. Anything else it refuses to guess at.
    /// </summary>
    private static ConstructorInfo? DeserializationConstructor(Type type)
    {
        ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        ConstructorInfo? annotated = Array.Find(
            constructors, candidate => candidate.GetCustomAttribute<JsonConstructorAttribute>() is not null);

        if (annotated is not null)
        {
            return annotated;
        }

        return constructors.Length == 1 && constructors[0].GetParameters().Length > 0
            ? constructors[0]
            : null;
    }

    /// <summary>The element type of a collection, or the type itself.</summary>
    private static Type Unwrap(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType() ?? type;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return underlying;
        }

        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
        {
            Type[] arguments = type.GetGenericArguments();
            return arguments.Length == 1 ? arguments[0] : type;
        }

        return type;
    }

    private static bool IsOurs(Type type) =>
        !type.IsPrimitive
        && !type.IsEnum
        && type != typeof(string)
        && type.Namespace?.StartsWith("DevBuddy", StringComparison.Ordinal) == true;

    /// <summary>
    /// Taken from the use cases themselves rather than from a list here, so a new operation is
    /// covered the moment it is written.
    /// </summary>
    private static IEnumerable<Type> UseCaseTypes() =>
        typeof(JsonConventions).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && BaseArguments(type).Length == 2);

    private static Type[] BaseArguments(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && string.Equals(current.Name, "UseCase`2", StringComparison.Ordinal))
            {
                return current.GetGenericArguments();
            }
        }

        return [];
    }
}
