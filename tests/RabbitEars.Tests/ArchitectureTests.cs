using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using RabbitEars.Core;

namespace RabbitEars.Tests;

/// <summary>
/// Holds <c>RabbitEars.Core</c> to what AGENTS.md says it is: the TV as a pure state machine, with no disk,
/// no web, no threads, and time and randomness injected so tests can single-step it.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Core = typeof(Television).Assembly;

    [Fact]
    public void The_television_has_no_disk_web_or_threads()
    {
        var result = Types.InAssembly(Core).That().ResideInNamespaceStartingWith("RabbitEars").ShouldNot().HaveDependencyOnAny(
            "System.IO", "System.Net", "System.Threading", "Microsoft.AspNetCore", "Microsoft.Extensions", "RabbitEars.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "RabbitEars.Core reaches outside the state machine; that belongs in RabbitEars.Api: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Calls that read the real clock or make an unseeded random. <c>Random.Shared</c> and
    /// <c>TimeProvider.System</c> stay allowed: they are the defaults <see cref="Television"/> takes until a
    /// test passes its own.
    /// </summary>
    private static readonly string[] Unsteady =
    [
        "System.DateTime System.DateTime::get_Now()",
        "System.DateTime System.DateTime::get_UtcNow()",
        "System.DateTime System.DateTime::get_Today()",
        "System.DateTimeOffset System.DateTimeOffset::get_Now()",
        "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()",
        "System.Void System.Random::.ctor()",
    ];

    [Fact]
    public void Time_and_randomness_are_injected()
    {
        using var module = ModuleDefinition.ReadModule(Core.Location);
        // Only our own types: coverage instrumentation adds a tracker type of its own that reads the clock.
        var found = module.GetTypes()
            .Where(t => Outermost(t).Namespace.StartsWith("RabbitEars", StringComparison.Ordinal))
            .SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(method => method.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Newobj)
                .Select(i => i.Operand).OfType<MethodReference>()
                .Where(called => Unsteady.Contains(called.FullName))
                .Select(called => $"{method.DeclaringType.FullName}.{method.Name} calls {called.FullName}"))
            .ToList();

        Assert.True(found.Count == 0, "Use the injected TimeProvider or Random instead:\n" + string.Join("\n", found));
    }

    /// <summary>Async methods and lambdas compile to nested types, which carry no namespace of their own.</summary>
    private static TypeDefinition Outermost(TypeDefinition type) => type.DeclaringType is { } outer ? Outermost(outer) : type;
}
