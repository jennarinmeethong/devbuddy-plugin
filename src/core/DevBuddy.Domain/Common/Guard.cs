namespace DevBuddy.Domain.Common;

/// <summary>
/// Argument checks that throw <see cref="DomainValidationException"/> rather than
/// ArgumentException, so a caller can distinguish a broken domain rule from a
/// programming error in infrastructure code.
/// </summary>
public static class Guard
{
    public static T NotNull<T>(T? value, string name)
        where T : class
    {
        if (value is null)
        {
            throw new DomainValidationException($"{name} is required.");
        }

        return value;
    }

    public static string NotBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{name} is required and cannot be blank.");
        }

        return value;
    }

    public static string NotLongerThan(string value, int maxLength, string name)
    {
        if (value.Length > maxLength)
        {
            throw new DomainValidationException(
                $"{name} is limited to {maxLength} characters but was {value.Length}.");
        }

        return value;
    }

    public static TEnum Defined<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new DomainValidationException($"{name} is not a defined {typeof(TEnum).Name} value.");
        }

        return value;
    }

    public static DateTimeOffset Utc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainValidationException($"{name} must be expressed in UTC.");
        }

        return value;
    }
}
