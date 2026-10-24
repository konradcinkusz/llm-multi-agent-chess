using System.Reflection;
using Chess.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests;

/// <summary>
/// The kernel (Chess.ServiceDefaults) carries no domain (03 §2, P2). These checks read the compiled assembly, so a
/// violation fails them however it was introduced.
/// </summary>
public class KernelArchitectureTests
{
    private const string KernelNamespace = "Chess.ServiceDefaults";

    private static readonly Assembly Kernel = typeof(Extensions).Assembly;

    [Fact]
    public void KernelAssemblyIsTheServiceDefaultsAssemblyAndHasTypes()
    {
        // Guards the other checks against passing vacuously on an empty or wrong assembly.
        Assert.Equal(KernelNamespace, Kernel.GetName().Name);
        Assert.NotEmpty(Kernel.GetExportedTypes());
    }

    [Fact]
    public void NoExportedTypeDerivesFromDbContext()
    {
        var offenders = Kernel.GetExportedTypes()
            .Where(type => typeof(DbContext).IsAssignableFrom(type))
            .Select(type => type.FullName);

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoExportedTypeLivesOutsideTheKernelNamespace()
    {
        var offenders = Kernel.GetExportedTypes()
            .Where(type => type.Namespace is { } ns
                && (ns == "Chess" || ns.StartsWith("Chess.", StringComparison.Ordinal))
                && ns != KernelNamespace
                && !ns.StartsWith(KernelNamespace + ".", StringComparison.Ordinal))
            .Select(type => type.FullName);

        Assert.Empty(offenders);
    }

    [Fact]
    public void KernelDoesNotReferenceEntityFramework()
    {
        var offenders = Kernel.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name == "Microsoft.EntityFrameworkCore"
                || name?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) == true);

        Assert.Empty(offenders);
    }

    [Fact]
    public void KernelReferencesNoOtherChessAssembly()
    {
        var offenders = Kernel.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name?.StartsWith("Chess.", StringComparison.Ordinal) == true && name != KernelNamespace);

        Assert.Empty(offenders);
    }
}
