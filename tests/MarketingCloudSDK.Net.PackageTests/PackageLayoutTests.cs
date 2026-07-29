using System.Xml.Linq;

namespace MarketingCloudSDK.Net.PackageTests;

/// <summary>
/// Asserts the shape of the produced NuGet packages.
/// </summary>
/// <remarks>
/// These run against the packed <c>.nupkg</c> rather than the build output, so they catch
/// packaging regressions the compiler cannot see - a target framework the merge step dropped, a
/// dependency group that came out empty or lost a pin's brackets, a licence file that stopped
/// being included.
/// </remarks>
public class PackageLayoutTests
{
    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_carries_an_assembly_for_every_expected_target_framework(string id)
    {
        using var package = Packages.OpenPackage(id);

        foreach (var tfm in Packages.TargetFrameworksOf(id))
        {
            Assert.True(
                package.GetEntry($"lib/{tfm}/{id}.dll") is not null,
                $"{id} is missing 'lib/{tfm}/{id}.dll'. The net10 assets come from the second pack " +
                "pass and are grafted in by merge-packages.py, so a missing net10 target framework " +
                "usually means that step did not run.");
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_carries_no_target_framework_it_should_not(string id)
    {
        using var package = Packages.OpenPackage(id);

        var expected = Packages.TargetFrameworksOf(id).ToHashSet();

        var actual = package.Entries
            .Select(entry => entry.FullName.Split('/'))
            .Where(parts => parts.Length > 2 && parts[0] == "lib")
            .Select(parts => parts[1])
            .ToHashSet();

        // Equality, not containment, in both directions: a target framework silently disappearing
        // from the merge step is as bad as one appearing that nothing was built or tested for -
        // and for the Maui package a net8/net9 android leg APPEARING would mean the AndroidX
        // generation conflict was papered over rather than resolved upstream (see Packages).
        Assert.Equal(expected.OrderBy(tfm => tfm), actual.OrderBy(tfm => tfm));
    }

    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_carries_documentation_for_every_assembly(string id)
    {
        using var package = Packages.OpenPackage(id);

        foreach (var tfm in Packages.TargetFrameworksOf(id))
        {
            var entry = package.GetEntry($"lib/{tfm}/{id}.xml");

            Assert.True(entry is not null, $"{id} is missing 'lib/{tfm}/{id}.xml'.");

            // The API is the product here, and its documentation is where every platform
            // difference is recorded. A few hundred bytes would mean the file was emitted but
            // empty.
            Assert.True(entry!.Length > 1_000, $"'{entry.FullName}' is only {entry.Length} bytes.");
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_declares_a_dependency_group_for_every_target_framework(string id)
    {
        using var package = Packages.OpenPackage(id);

        var groups = DependencyGroups(Packages.ReadNuspec(package, id));

        foreach (var tfm in Packages.TargetFrameworksOf(id))
        {
            Assert.True(
                groups.ContainsKey(tfm),
                $"{id}'s nuspec declares no dependency group for '{tfm}'. NuGet reads a missing " +
                "group as 'this target framework needs nothing', so the dependencies would not " +
                "be restored and the app would fail with the native SDK missing.");
        }
    }

    [Fact]
    public void Every_facade_group_pins_the_core_umbrella_exactly()
    {
        using var package = Packages.OpenPackage("MarketingCloudSDK.Net");

        var groups = DependencyGroups(Packages.ReadNuspec(package, "MarketingCloudSDK.Net"));

        foreach (var tfm in Packages.FacadeTargetFrameworks)
        {
            // ALL heads, the neutral ones included: ISfmcIdentity is part of the façade's
            // public surface, so a shared class library compiling against the neutral assembly
            // needs SFMCSDK.Net too. This is the one deliberate divergence from the core
            // umbrella's own shape, whose neutral groups are empty.
            var core = Assert.Single(groups[tfm], dependency => dependency.Id == "SFMCSDK.Net");

            Assert.Equal($"[{Packages.CoreUmbrellaVersion}]", core.Version);
        }
    }

    [Fact]
    public void Android_facade_assets_depend_on_the_Android_binding_at_its_exact_pin()
    {
        using var package = Packages.OpenPackage("MarketingCloudSDK.Net");

        var groups = DependencyGroups(Packages.ReadNuspec(package, "MarketingCloudSDK.Net"));

        foreach (var tfm in Packages.FacadeTargetFrameworks.Where(t => t.Contains("android")))
        {
            Assert.Equal(2, groups[tfm].Count);

            var binding = Assert.Single(groups[tfm], d => d.Id == "MarketingCloudSDK.Net.Android");

            // The EXACT bracketed range, not a floating minimum: the façade calls the binding's
            // hand-written convenience layer, which carries no compatibility promise across
            // revisions. A bare version here would mean the pack silently loosened the pin.
            Assert.Equal($"[{Packages.AndroidBindingVersion}]", binding.Version);
        }
    }

    [Fact]
    public void iOS_facade_assets_depend_on_the_iOS_binding_at_its_exact_pin()
    {
        using var package = Packages.OpenPackage("MarketingCloudSDK.Net");

        var groups = DependencyGroups(Packages.ReadNuspec(package, "MarketingCloudSDK.Net"));

        foreach (var tfm in Packages.FacadeTargetFrameworks.Where(t => t.Contains("ios")))
        {
            Assert.Equal(2, groups[tfm].Count);

            var binding = Assert.Single(groups[tfm], d => d.Id == "MarketingCloudSDK.Net.iOS");

            Assert.Equal($"[{Packages.IosBindingVersion}]", binding.Version);
        }
    }

    [Fact]
    public void Neutral_facade_assets_depend_on_the_core_umbrella_and_nothing_else()
    {
        using var package = Packages.OpenPackage("MarketingCloudSDK.Net");

        var groups = DependencyGroups(Packages.ReadNuspec(package, "MarketingCloudSDK.Net"));

        foreach (var tfm in Packages.NeutralTargetFrameworks)
        {
            // The whole point of the neutral asset is that a Windows head (or a unit test) can
            // restore it - the core umbrella restores there too (it has neutral assets of its
            // own), but a stray platform binding dependency would fail the restore with NU1202,
            // and nothing in the build would notice.
            var dependency = Assert.Single(groups[tfm]);

            Assert.Equal("SFMCSDK.Net", dependency.Id);
        }
    }

    [Fact]
    public void Maui_package_pins_the_facade_exactly_and_floors_Controls_per_band()
    {
        using var package = Packages.OpenPackage("MarketingCloudSDK.Net.Maui");

        var groups = DependencyGroups(Packages.ReadNuspec(package, "MarketingCloudSDK.Net.Maui"));

        foreach (var tfm in Packages.MauiTargetFrameworks)
        {
            var facade = Assert.Single(groups[tfm], d => d.Id == "MarketingCloudSDK.Net");

            // Exact: the two packages release together from one commit, and a floating minimum
            // would let NuGet pair this thin layer with a façade it was never built against.
            Assert.Equal($"[{Packages.Version}]", facade.Version);

            var controls = Assert.Single(groups[tfm], d => d.Id == "Microsoft.Maui.Controls");

            // A floor (no brackets), per band - the oldest Controls of the line the leg is
            // built against, so consumers on anything newer resolve their own version. The
            // band-specific values guard against the drift the csproj documents: a floor taken
            // from the packing machine's workload would move with CI images.
            var expectedControls = tfm.StartsWith("net8.0", StringComparison.Ordinal) ? "8.0.100"
                : tfm.StartsWith("net9.0", StringComparison.Ordinal) ? "9.0.30"
                : "10.0.0";
            Assert.Equal(expectedControls, controls.Version);

            // The AndroidX restore aid must have stayed private: it is a resolution fact of
            // this repository's own build, not a pin consumers inherit.
            Assert.DoesNotContain(groups[tfm], d => d.Id.StartsWith("Xamarin.AndroidX", StringComparison.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_declares_plain_MIT_and_ships_only_that_text(string id)
    {
        using var package = Packages.OpenPackage(id);

        var nuspec = XDocument.Parse(Packages.ReadNuspec(package, id));
        var ns = nuspec.Root!.GetDefaultNamespace();

        // Plain MIT, unlike the binding packages' "MIT AND BSD-3-Clause" - these packages ship
        // no native binaries, so Salesforce's licence is not theirs to declare. The BSD text
        // arrives with the binding packages, each of which packs both. If this assertion ever
        // needs to change, native payloads have crept into the umbrella and the licence note in
        // Directory.Build.props needs rewriting with it.
        Assert.Equal("MIT", nuspec.Descendants(ns + "license").Single().Value);
        Assert.NotNull(package.GetEntry("licenses/LICENSE"));
        Assert.Null(package.GetEntry("licenses/BSD-3-Clause-Salesforce.txt"));
    }

    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_carries_a_readme_and_an_icon(string id)
    {
        using var package = Packages.OpenPackage(id);

        Assert.NotNull(package.GetEntry("README.md"));
        Assert.NotNull(package.GetEntry("icon.png"));
    }

    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_has_a_symbol_package_with_pdbs_for_every_target_framework(string id)
    {
        var path = Path.Combine(Packages.ArtifactsDirectory, $"{id}.{Packages.Version}.snupkg");

        Assert.True(File.Exists(path), $"'{path}' does not exist.");

        using var symbols = System.IO.Compression.ZipFile.OpenRead(path);

        foreach (var tfm in Packages.TargetFrameworksOf(id))
        {
            // The snupkg goes through the same two-pass merge as the nupkg, so its net10 pdbs
            // are grafted in too - and a merge that only handled .nupkg files would strand every
            // net10 consumer without symbols while looking complete from the outside.
            Assert.True(
                symbols.GetEntry($"lib/{tfm}/{id}.pdb") is not null,
                $"{id}.snupkg is missing 'lib/{tfm}/{id}.pdb'.");
        }
    }

    [Theory]
    [MemberData(nameof(Packages.Ids), MemberType = typeof(Packages))]
    public void Package_depends_on_its_manifest_siblings(string id)
    {
        using var package = Packages.OpenPackage(id);

        var groups = DependencyGroups(Packages.ReadNuspec(package, id));

        foreach (var sibling in Packages.Spec(id).Dependencies)
        {
            foreach (var tfm in Packages.TargetFrameworksOf(id))
            {
                Assert.True(
                    groups[tfm].Any(d => d.Id == sibling),
                    $"{id}'s '{tfm}' group does not depend on {sibling}, but packages.tsv says it must.");
            }
        }
    }

    /// <summary>Maps each target framework to the dependencies its nuspec group declares.</summary>
    private static Dictionary<string, IReadOnlyList<(string Id, string Version)>> DependencyGroups(string nuspec)
    {
        var document = XDocument.Parse(nuspec);
        var ns = document.Root!.GetDefaultNamespace();

        return document
            .Descendants(ns + "group")
            .ToDictionary(
                group => group.Attribute("targetFramework")!.Value,
                group => (IReadOnlyList<(string, string)>)
                    [.. group.Elements(ns + "dependency")
                        .Select(dependency => (
                            dependency.Attribute("id")!.Value,
                            dependency.Attribute("version")!.Value))]);
    }
}
