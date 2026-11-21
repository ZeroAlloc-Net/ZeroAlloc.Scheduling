namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// The generator must load on the .NET 10 SDK, whose newest band ships Roslyn 5.9. Built against a
/// newer Roslyn, it fails there with CS9057 and generates nothing, as ZeroAlloc.Scheduling 2.4.0 did.
/// </summary>
public sealed class RoslynFloorTests
{
    private static readonly Version Floor = new(5, 9, 0, 0);

    [Fact]
    public void Generator_References_No_Roslyn_Newer_Than_The_DotNet10_Sdk_Ships()
    {
        var roslyn = typeof(SchedulingGenerator).Assembly.GetReferencedAssemblies()
            .Where(a => a.Name is "Microsoft.CodeAnalysis" or "Microsoft.CodeAnalysis.CSharp")
            .ToList();

        roslyn.Should().NotBeEmpty();
        roslyn.Should().OnlyContain(a => a.Version! <= Floor,
            "a consumer on the .NET 10 SDK cannot load a generator built against a newer Roslyn");
    }
}
