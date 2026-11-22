namespace ZeroAlloc.Scheduling.Generator.Tests;

/// <summary>
/// The generator must load on every .NET 10 SDK, whose oldest band, 10.0.1xx, ships Roslyn 5.0. Built
/// against a newer Roslyn, it fails there with CS9057 and generates nothing, as ZeroAlloc.Scheduling
/// 2.4.0 did.
/// </summary>
public sealed class RoslynFloorTests
{
    private static readonly Version Floor = new(5, 0, 0, 0);

    [Fact]
    public void Generator_References_No_Roslyn_Newer_Than_The_Oldest_DotNet10_Sdk_Ships()
    {
        var roslyn = typeof(SchedulingGenerator).Assembly.GetReferencedAssemblies()
            .Where(a => a.Name is "Microsoft.CodeAnalysis" or "Microsoft.CodeAnalysis.CSharp")
            .ToList();

        roslyn.Should().NotBeEmpty();
        roslyn.Should().OnlyContain(a => a.Version! <= Floor,
            "a consumer on the oldest .NET 10 SDK band cannot load a generator built against a newer Roslyn");
    }
}
