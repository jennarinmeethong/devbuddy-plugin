using System.Reflection;
using System.Runtime.CompilerServices;

namespace DevBuddy.Application.Tests;

/// <summary>
/// info.md: Change Request and Code Review are separate concepts and separate record types, and
/// the ambiguous abbreviation CR must not be used as the shared identifier for both.
/// <para>
/// This is the kind of rule that erodes quietly. Someone adds CrId or a CRType column six months
/// from now, it looks harmless in review, and the distinction the whole system depends on is
/// gone. So it is checked here rather than remembered.
/// </para>
/// </summary>
public sealed class NamingRuleTests
{
    private static readonly string[] ProductAssemblies =
    [
        "DevBuddy.Domain",
        "DevBuddy.Application",
        "DevBuddy.Infrastructure",
    ];

    [Fact]
    public void the_abbreviation_cr_is_not_used_as_an_identifier()
    {
        List<string> violations = [];

        foreach (string assemblyName in ProductAssemblies)
        {
            Assembly assembly = RepositoryLayout.Load(assemblyName);

            foreach (Type type in assembly.GetTypes().Where(IsAuthored))
            {
                Inspect(violations, assemblyName, type.FullName ?? type.Name);

                foreach (MemberInfo member in type.GetMembers(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(IsAuthored))
                {
                    Inspect(violations, assemblyName, $"{type.Name}.{member.Name}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "The abbreviation CR must not be used as an identifier. Write change_request or "
            + "code_review in full. Found: " + string.Join(", ", violations));
    }

    [Fact]
    public void both_concepts_are_spelled_out_in_the_work_item_type_enum()
    {
        Type workItemType = RepositoryLayout.Load("DevBuddy.Domain")
            .GetTypes()
            .Single(type => type.Name == "WorkItemType");

        string[] names = Enum.GetNames(workItemType);

        Assert.Contains("ChangeRequest", names);
        Assert.Contains("CodeReview", names);
    }

    /// <summary>
    /// Matches the abbreviation as an acronym in a PascalCase identifier: CR at the start or
    /// after a word break, and not the start of an ordinary word such as Created or Correction.
    /// </summary>
    private static bool UsesTheAbbreviation(string identifier)
    {
        for (int index = 0; index + 1 < identifier.Length; index++)
        {
            if (identifier[index] != 'C' || identifier[index + 1] != 'R')
            {
                continue;
            }

            bool nextStartsANewWord =
                index + 2 >= identifier.Length
                || char.IsUpper(identifier[index + 2])
                || identifier[index + 2] == '_';

            if (nextStartsANewWord)
            {
                return true;
            }
        }

        return false;
    }

    private static void Inspect(List<string> violations, string assemblyName, string identifier)
    {
        if (UsesTheAbbreviation(identifier))
        {
            violations.Add($"{assemblyName}: {identifier}");
        }
    }

    private static bool IsAuthored(MemberInfo member) =>
        !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        && !member.Name.Contains('<', StringComparison.Ordinal);
}
