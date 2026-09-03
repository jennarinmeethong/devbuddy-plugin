using System.Text;
using System.Text.RegularExpressions;
using DevBuddy.Application.Abstractions;

namespace DevBuddy.Infrastructure.Scanning;

/// <summary>
/// Finds government ID numbers, payment card numbers, and explicitly labelled personal fields.
/// <para>
/// Control SB-18. Unlike the secret scanner, whether a finding here blocks anything depends on
/// where the pipeline is — the AI channel and whether the project has an approved bounded scope —
/// which is why this type reports findings and nothing else. Deciding what to do about them is the
/// pipeline's job, not the scanner's.
/// </para>
/// </summary>
internal sealed class PersonalDataScanner : IPersonalDataScanner
{
    public Task<PersonalDataScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
        Task.FromResult(Scan(content));

    public static PersonalDataScanResult Scan(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return PersonalDataScanResult.Clean;
        }

        List<PersonalDataFinding> findings = [];

        foreach (PersonalDataRule rule in PersonalDataRules.All)
        {
            foreach (Match match in rule.Pattern.Matches(content))
            {
                string value = ValueOf(match);

                if (PersonalDataRules.IsAlreadyRedacted(value) || !rule.Accepts(value))
                {
                    continue;
                }

                findings.Add(new PersonalDataFinding(rule.Name, LineOf(content, match.Index), match.Length));
            }
        }

        return findings.Count == 0
            ? PersonalDataScanResult.Clean
            : new PersonalDataScanResult([.. findings.OrderBy(finding => finding.LineNumber)]);
    }

    internal static string ValueOf(Match match)
    {
        Group value = match.Groups["value"];
        string text = value.Success ? value.Value : match.Value;
        return text.Trim('"', '\'');
    }

    private static int LineOf(string content, int index)
    {
        int line = 1;

        for (int position = 0; position < index && position < content.Length; position++)
        {
            if (content[position] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

/// <summary>
/// Replaces what the scanner finds. A labelled field keeps its label and loses its value, the same
/// trade the secret redactor makes for a named assignment; a bare ID or card number goes whole.
/// </summary>
internal sealed class PersonalDataRedactor : IPersonalDataRedactor
{
    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string current = text;

        foreach (PersonalDataRule rule in PersonalDataRules.All)
        {
            current = rule.Pattern.Replace(current, match => Replace(rule, match));
        }

        return current;
    }

    private static string Replace(PersonalDataRule rule, Match match)
    {
        string value = PersonalDataScanner.ValueOf(match);

        if (PersonalDataRules.IsAlreadyRedacted(value) || !rule.Accepts(value))
        {
            return match.Value;
        }

        Group valueGroup = match.Groups["value"];

        if (!valueGroup.Success)
        {
            return PersonalDataRules.Marker;
        }

        var builder = new StringBuilder(match.Value.Length);
        int valueStart = valueGroup.Index - match.Index;

        builder.Append(match.Value.AsSpan(0, valueStart));
        builder.Append(PersonalDataRules.Marker);
        builder.Append(match.Value.AsSpan(valueStart + valueGroup.Length));

        return builder.ToString();
    }
}
