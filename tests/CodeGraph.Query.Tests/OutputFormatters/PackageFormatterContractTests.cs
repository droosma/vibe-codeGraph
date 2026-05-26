using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class PackageFormatterContractTests
{
    [Fact]
    public void FormatUsage_OrdersProjectsPackagesAndOptionalSectionsExactly()
    {
        var usages = new List<PackageUsage>
        {
            new("Zeta.Pkg", "2.0.0", "beta", 1, 4, new[] { "Beta.Service" }, new[] { "Zeta.Type" }),
            new("Alpha.Pkg", null, "Alpha", 3, 2, Array.Empty<string>(), Array.Empty<string>()),
            new("Gamma.Pkg", "1.2.3", "Alpha", 5, 6, new[] { "Alpha.Service", "Alpha.Controller" }, new[] { "Gamma.Type", "Gamma.Other" })
        };

        var output = PackageFormatter.FormatUsage(usages);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Package usage (3 entries)",
            string.Empty,
            "## Alpha",
            "- Alpha.Pkg",
            "  Types: 3, Usages: 2",
            "- Gamma.Pkg v1.2.3",
            "  Types: 5, Usages: 6",
            "  External symbols: Gamma.Type, Gamma.Other",
            "  Internal users: Alpha.Service, Alpha.Controller",
            string.Empty,
            "## beta",
            "- Zeta.Pkg v2.0.0",
            "  Types: 1, Usages: 4",
            "  External symbols: Zeta.Type",
            "  Internal users: Beta.Service"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void FormatDependents_OrdersProjectsConsumersAndOptionalSymbolsExactly()
    {
        var dependents = new List<PackageDependent>
        {
            new("Pkg.Core", "1.0.0", "beta", "beta.Zeta", "Zeta", NodeKind.Type, new[] { "Pkg.Core.Zeta" }),
            new("Pkg.Core", "1.0.0", "Alpha", "Alpha.Run", "Run", NodeKind.Method, Array.Empty<string>()),
            new("Pkg.Core", "1.0.0", "Alpha", "Alpha.Type", "Type", NodeKind.Type, new[] { "Pkg.Core.Feature", "Pkg.Core.Helper" })
        };

        var output = PackageFormatter.FormatDependents(dependents);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Package dependents for Pkg.Core (3 entries)",
            string.Empty,
            "## Alpha",
            "- Type: Alpha.Type",
            "  External symbols: Pkg.Core.Feature, Pkg.Core.Helper",
            "- Method: Alpha.Run",
            string.Empty,
            "## beta",
            "- Type: beta.Zeta",
            "  External symbols: Pkg.Core.Zeta"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void FormatConflicts_OrdersPackagesAndProjectsExactly()
    {
        var conflicts = new List<PackageConflict>
        {
            new("zeta.pkg", new Dictionary<string, string?>
            {
                ["ProjectB"] = "2.0.0",
                ["ProjectA"] = null
            }),
            new("Alpha.Pkg", new Dictionary<string, string?>
            {
                ["beta"] = "1.1.0",
                ["Alpha"] = "1.0.0"
            })
        };

        var output = PackageFormatter.FormatConflicts(conflicts);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Version conflicts (2 packages)",
            string.Empty,
            "## Alpha.Pkg",
            "- Alpha: 1.0.0",
            "- beta: 1.1.0",
            string.Empty,
            "## zeta.pkg",
            "- ProjectA: (unknown)",
            "- ProjectB: 2.0.0"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void FormatUsageAsJson_WritesIndentedCamelCaseJson()
    {
        var usages = new List<PackageUsage>
        {
            new("Pkg.Core", "1.0.0", "App", 2, 3, new[] { "App.Service" }, new[] { "Pkg.Core.Feature" })
        };

        var json = PackageFormatter.FormatUsageAsJson(usages);

        Assert.Contains($"[{Environment.NewLine}  {{", json);
        Assert.Contains($"{Environment.NewLine}    \"packageId\": \"Pkg.Core\"", json);
        Assert.DoesNotContain("\"PackageId\"", json);
    }
}
