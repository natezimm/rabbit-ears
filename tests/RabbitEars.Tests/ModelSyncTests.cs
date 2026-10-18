using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

/// <summary>
/// The web app's <c>models.ts</c> is written by hand to match what the server
/// sends. These tests read that file and compare it with the C# types as they
/// go over the wire, so a field added, renamed or retyped on one side fails CI
/// until the other side matches.
/// </summary>
public sealed partial class ModelSyncTests
{
    /// <summary>Every interface in models.ts and the server type it mirrors.</summary>
    private static readonly Dictionary<string, Type> Mirrors = new()
    {
        ["ChannelInfo"] = typeof(ChannelInfo),
        ["NowPlaying"] = typeof(NowPlaying),
        ["OsdMessage"] = typeof(OsdMessage),
        ["ChannelListing"] = typeof(ChannelListing),
        ["TvSnapshot"] = typeof(TvSnapshot),
        ["CrtConfig"] = typeof(CrtOptions),
        ["PictureConfig"] = typeof(PictureOptions),
        ["DemoInfo"] = typeof(DemoInfo),
        ["ChannelEdit"] = typeof(ChannelEdit),
        ["AdminEpisode"] = typeof(AdminEpisode),
        ["AdminChannel"] = typeof(AdminChannel),
        ["AdminCatalog"] = typeof(AdminCatalog),
        ["DemoCredit"] = typeof(DemoCredit),
    };

    private static readonly NullabilityInfoContext Nullability = new();

    private static readonly Lazy<string> Source = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RabbitEars.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "web", "src", "app", "core", "models.ts"));
    });

    [Fact]
    public void Every_interface_in_models_ts_mirrors_a_server_type()
    {
        var unmapped = Interfaces().Keys.Except(Mirrors.Keys).ToList();

        Assert.True(unmapped.Count == 0,
            $"models.ts has interfaces with no server type listed in {nameof(ModelSyncTests)}.{nameof(Mirrors)}: "
            + string.Join(", ", unmapped));
    }

    [Fact]
    public void Interfaces_match_the_server_types_field_for_field()
    {
        var interfaces = Interfaces();
        var problems = new List<string>();

        foreach (var (name, type) in Mirrors)
        {
            if (!interfaces.TryGetValue(name, out var members))
            {
                problems.Add($"models.ts is missing interface {name} (mirrors {type.Name}).");
                continue;
            }

            Compare(name, members, type, problems);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Play_reasons_match_the_server_constants()
    {
        var reason = Interfaces()["NowPlaying"]["reason"];
        var server = typeof(PlayReason)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!);

        Assert.Equal(server.Order(StringComparer.Ordinal), StringLiterals(reason).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Default_picture_matches_the_server_defaults()
    {
        // The literal is plain enough to become JSON with three substitutions.
        var literal = DefaultPictureRegex().Match(Source.Value).Groups["body"].Value;
        Assert.NotEmpty(literal);
        var json = literal.Replace('\'', '"');
        json = BareKeyRegex().Replace(json, "\"$1\":");
        json = TrailingCommaRegex().Replace(json, "$1");

        var web = JsonNode.Parse(json);
        var server = JsonSerializer.SerializeToNode(new PictureOptions(), JsonSerializerOptions.Web);

        Assert.True(JsonNode.DeepEquals(web, server),
            $"DEFAULT_PICTURE in models.ts differs from new PictureOptions().{Environment.NewLine}"
            + $"models.ts: {web?.ToJsonString()}{Environment.NewLine}server:    {server?.ToJsonString()}");
    }

    /// <summary>Checks one TS object type against the properties a CLR type serializes.</summary>
    private static void Compare(string where, Dictionary<string, string> members, Type type, List<string> problems)
    {
        var info = JsonSerializerOptions.Web.GetTypeInfo(type);

        foreach (var property in info.Properties)
        {
            var at = $"{where}.{property.Name}";
            if (!members.TryGetValue(property.Name, out var tsType))
            {
                problems.Add($"{at} is sent by the server ({type.Name}) but missing from models.ts.");
                continue;
            }

            var clrProperty = (PropertyInfo)property.AttributeProvider!;
            var nullable = Nullability.Create(clrProperty).ReadState == NullabilityState.Nullable;
            var (tsCore, tsNullable) = SplitNull(tsType);
            if (nullable != tsNullable)
            {
                problems.Add($"{at} is {(nullable ? "" : "not ")}nullable on the server but `{tsType}` in models.ts.");
            }

            CompareType(at, tsCore, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, problems);
        }

        foreach (var extra in members.Keys.Except(info.Properties.Select(p => p.Name)))
        {
            problems.Add($"{where}.{extra} is in models.ts but the server ({type.Name}) never sends it.");
        }
    }

    private static void CompareType(string at, string tsType, Type clr, List<string> problems)
    {
        if (clr == typeof(string))
        {
            if (tsType != "string" && StringLiterals(tsType).Count == 0)
            {
                problems.Add($"{at} is a string on the server but `{tsType}` in models.ts.");
            }
        }
        else if (clr == typeof(bool))
        {
            Expect(at, tsType, "boolean", problems);
        }
        else if (clr == typeof(int) || clr == typeof(long) || clr == typeof(double))
        {
            Expect(at, tsType, "number", problems);
        }
        else if (ElementType(clr) is { } element)
        {
            if (!tsType.EndsWith("[]", StringComparison.Ordinal))
            {
                problems.Add($"{at} is a list on the server but `{tsType}` in models.ts.");
                return;
            }

            CompareType(at + "[]", tsType[..^2], element, problems);
        }
        else if (tsType.StartsWith('{'))
        {
            Compare(at, Members(tsType[1..^1]), clr, problems);
        }
        else if (Mirrors.TryGetValue(tsType, out var mirrored))
        {
            if (mirrored != clr)
            {
                problems.Add($"{at} is a {clr.Name} on the server but `{tsType}` (mirrors {mirrored.Name}) in models.ts.");
            }
        }
        else
        {
            problems.Add($"{at} is a {clr.Name} on the server but `{tsType}` in models.ts, which this test can't match.");
        }
    }

    private static void Expect(string at, string tsType, string expected, List<string> problems)
    {
        if (tsType != expected)
        {
            problems.Add($"{at} should be `{expected}` in models.ts but is `{tsType}`.");
        }
    }

    private static Type? ElementType(Type clr) =>
        clr.IsArray ? clr.GetElementType()
        : clr.IsGenericType && clr.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? clr.GetGenericArguments()[0]
        : null;

    private static (string Core, bool Nullable) SplitNull(string tsType)
    {
        var parts = tsType.Split('|', StringSplitOptions.TrimEntries).ToList();
        var nullable = parts.Remove("null");
        return (string.Join(" | ", parts), nullable);
    }

    /// <summary>The values of a union like <c>'a' | 'b'</c>; empty if it is anything else.</summary>
    private static List<string> StringLiterals(string tsType)
    {
        var parts = tsType.Split('|', StringSplitOptions.TrimEntries);
        return parts.All(p => p.Length >= 2 && p[0] == '\'' && p[^1] == '\'')
            ? parts.Select(p => p[1..^1]).ToList()
            : [];
    }

    private static Dictionary<string, Dictionary<string, string>> Interfaces()
    {
        var code = CommentRegex().Replace(Source.Value, "");
        var result = new Dictionary<string, Dictionary<string, string>>();

        foreach (Match match in InterfaceRegex().Matches(code))
        {
            var open = match.Index + match.Length - 1;
            var close = MatchingBrace(code, open);
            result[match.Groups["name"].Value] = Members(code[(open + 1)..close]);
        }

        return result;
    }

    /// <summary>Splits an object type body into <c>name: type</c> pairs, keeping inline object types whole.</summary>
    private static Dictionary<string, string> Members(string body)
    {
        var members = new Dictionary<string, string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i <= body.Length; i++)
        {
            var c = i < body.Length ? body[i] : ';';
            if (c == '{') depth++;
            else if (c == '}') depth--;
            else if ((c == ';' || c == ',') && depth == 0)
            {
                var member = body[start..i].Trim();
                start = i + 1;
                if (member.Length == 0) continue;

                var colon = member.IndexOf(':', StringComparison.Ordinal);
                members[member[..colon].Trim()] = Regex.Replace(member[(colon + 1)..].Trim(), @"\s+", " ");
            }
        }

        return members;
    }

    private static int MatchingBrace(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return i;
        }

        throw new InvalidOperationException("Unbalanced braces in models.ts.");
    }

    [GeneratedRegex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"export interface (?<name>\w+)\s*\{")]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex(@"DEFAULT_PICTURE: PictureConfig = (?<body>\{.*?\n\});", RegexOptions.Singleline)]
    private static partial Regex DefaultPictureRegex();

    [GeneratedRegex(@"(?<=[{,]\s*)(\w+):")]
    private static partial Regex BareKeyRegex();

    [GeneratedRegex(@",(\s*[}\]])")]
    private static partial Regex TrailingCommaRegex();
}
