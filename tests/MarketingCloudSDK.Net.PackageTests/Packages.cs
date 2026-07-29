using System.IO.Compression;

namespace MarketingCloudSDK.Net.PackageTests;

/// <summary>What one package in this repository is supposed to be.</summary>
/// <param name="Id">The NuGet package id, which is also its directory under <c>src/</c>.</param>
/// <param name="Dependencies">Other packages in this repository it must declare a dependency on.</param>
public sealed record PackageSpec(string Id, IReadOnlyList<string> Dependencies);

/// <summary>
/// Locates the packed <c>.nupkg</c> files and describes what each one is supposed to contain.
/// </summary>
/// <remarks>
/// The package set is read from <c>build/packages.tsv</c> rather than repeated here, so a package
/// added to the build but not to the tests fails as a missing file instead of passing unnoticed.
/// The per-package target-framework sets ARE repeated here, deliberately: they are the assertion.
/// </remarks>
public static class Packages
{
    /// <summary>Every package this repository builds, in dependency order.</summary>
    public static readonly IReadOnlyList<PackageSpec> All = ReadManifest();

    /// <summary>
    /// The nine target frameworks the façade must carry an assembly for.
    /// </summary>
    /// <remarks>
    /// The neutral <c>net8.0</c>, <c>net9.0</c> and <c>net10.0</c> entries are the ones worth
    /// asserting hardest. They are what lets a MAUI app's Windows head, and a unit test, restore
    /// the package at all, and nothing about the build would fail if the platform-neutral leg
    /// silently stopped producing them - the platform assets would still be there and the package
    /// would still look complete.
    /// </remarks>
    public static readonly string[] FacadeTargetFrameworks =
    [
        "net8.0",
        "net8.0-android34.0",
        "net8.0-ios18.0",
        "net9.0",
        "net9.0-android35.0",
        "net9.0-ios18.0",
        "net10.0",
        "net10.0-android36.0",
        "net10.0-ios26.0",
    ];

    /// <summary>
    /// The Maui package's target frameworks: iOS on every band, Android on net10 only - the
    /// measured consequence of MAUI 8/9's AndroidX generation being irreconcilable with
    /// MobilePush 11's (see the csproj), asserted here so the asymmetry stays deliberate. A
    /// net8/net9 android asset APPEARING would be as wrong as one disappearing: it would mean
    /// somebody restored the leg without the conflict having actually gone away upstream.
    /// </summary>
    public static readonly string[] MauiTargetFrameworks =
    [
        "net8.0-ios18.0",
        "net9.0-ios18.0",
        "net10.0-android36.0",
        "net10.0-ios26.0",
    ];

    /// <summary>The façade's neutral target frameworks, whose dependency group must carry the core umbrella only.</summary>
    public static readonly string[] NeutralTargetFrameworks = ["net8.0", "net9.0", "net10.0"];

    /// <summary>Per-package expected target frameworks.</summary>
    public static string[] TargetFrameworksOf(string id) => id switch
    {
        "MarketingCloudSDK.Net" => FacadeTargetFrameworks,
        "MarketingCloudSDK.Net.Maui" => MauiTargetFrameworks,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id,
            "Unknown package - add its expected target frameworks here alongside its packages.tsv row."),
    };

    /// <summary>
    /// The pins read from <c>Directory.Build.props</c> - the same properties the pack reads, so
    /// the assertions cannot drift from the build.
    /// </summary>
    public static string AndroidBindingVersion => Prop("SfmcAndroidPackageVersion");

    /// <inheritdoc cref="AndroidBindingVersion"/>
    public static string IosBindingVersion => Prop("SfmcIosPackageVersion");

    /// <inheritdoc cref="AndroidBindingVersion"/>
    public static string CoreUmbrellaVersion => Prop("SfmcCorePackageVersion");

    /// <summary>xunit member data: one row per package.</summary>
    public static TheoryData<string> Ids
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var package in All)
            {
                data.Add(package.Id);
            }

            return data;
        }
    }

    public static PackageSpec Spec(string id) => All.Single(package => package.Id == id);

    public static ZipArchive OpenPackage(string id)
    {
        var path = Path.Combine(ArtifactsDirectory, $"{id}.{Version}.nupkg");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"'{path}' does not exist. Run ./build/BuildNugets.sh first.", path);
        }

        return ZipFile.OpenRead(path);
    }

    public static string ReadNuspec(ZipArchive package, string id)
    {
        var entry = package.GetEntry($"{id}.nuspec")
            ?? throw new InvalidOperationException($"{id} has no {id}.nuspec.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The version the packages were built with, read from <c>Directory.Build.props</c>.
    /// </summary>
    /// <remarks>
    /// Overridable so a CI job that packed a prerelease can point the tests at it without the
    /// version being written down in two places.
    /// </remarks>
    public static string Version =>
        Environment.GetEnvironmentVariable("SFMC_PACKAGE_VERSION") is { Length: > 0 } configured
            ? configured
            : $"{Prop("SfmcNativeVersion")}.{Prop("SfmcBindingRevision")}";

    /// <summary>The directory packages are read from.</summary>
    public static string ArtifactsDirectory =>
        Environment.GetEnvironmentVariable("SFMC_ARTIFACTS_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(RepositoryRoot, "artifacts");

    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MarketingCloudSDK.Net.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException(
                    "Could not find the repository root by walking up from " + AppContext.BaseDirectory);
        }
    }

    private static IReadOnlyList<PackageSpec> ReadManifest()
    {
        var path = Path.Combine(RepositoryRoot, "build", "packages.tsv");
        var specs = new List<PackageSpec>();

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 3)
            {
                throw new InvalidOperationException($"Malformed row in packages.tsv: '{line}'");
            }

            var dependencies = columns[2] == "-"
                ? []
                : columns[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            specs.Add(new PackageSpec(columns[0], dependencies));
        }

        if (specs.Count == 0)
        {
            throw new InvalidOperationException("packages.tsv listed no packages.");
        }

        return specs;
    }

    private static string Prop(string name)
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        var open = $"<{name}>";
        var close = $"</{name}>";

        var start = props.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Directory.Build.props has no {open}");
        }

        start += open.Length;
        var end = props.IndexOf(close, start, StringComparison.Ordinal);

        return props[start..end].Trim();
    }
}
