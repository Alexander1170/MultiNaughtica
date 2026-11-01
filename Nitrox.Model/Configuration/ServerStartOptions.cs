using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration.Validators;

namespace Nitrox.Model.Configuration;

/// <summary>
///     Configuration with the information needed to start a Nitrox game server.
/// </summary>
public sealed partial record ServerStartOptions
{
    [ConfigurationKeyName("save")]
    [Required]
    [SaveName]
    public string SaveName { get; set; } = "My World";

    /// <summary>
    ///     Gets or sets the root directory containing the game files.
    /// </summary>
    [ConfigurationKeyName("game-path")]
    [Required]
    [DirectoryPath]
    [RootedPath]
    public string? GamePath { get; set; }

    /// <summary>
    ///     Gets or sets the location where Nitrox will store save and cache files.
    /// </summary>
    [ConfigurationKeyName("data-path")]
    [Required]
    [DirectoryPath]
    [RootedPath]
    public string? NitroxAppDataPath { get; set; }

    /// <summary>
    ///     Gets or sets the location of the Nitrox assets. Defaults to the root path of the Nitrox installation.
    /// </summary>
    [ConfigurationKeyName("assets-path")]
    [Required]
    [DirectoryPath]
    [RootedPath]
    public string? NitroxAssetsPath { get; set; }

    [ConfigurationKeyName("embedded")]
    public bool IsEmbedded { get; set; }

    /// <summary>
    ///     Gets or sets whether the server only reports what the world repair would do (<c>--heal-dry-run=true</c>). The report is written to the logs folder
    ///     (<c>world-repair-&lt;save&gt;-&lt;time&gt;.txt</c>) and summarized in the server log; the world folder is not touched: no repair, no backup, and no save while it runs.
    ///     Off by default. Run it on a copy of a world before the world itself is started with a new build.
    /// </summary>
    /// <remarks>
    ///     Write the value after an '=': the command line provider reads the next argument as the value of a switch that has none, so <c>--embedded --heal-dry-run true</c>
    ///     sets <see cref="IsEmbedded" /> from the text "--heal-dry-run", never sets this property, and the bind fails.
    /// </remarks>
    [ConfigurationKeyName("heal-dry-run")]
    public bool HealDryRun { get; set; }

    [OptionsValidator]
    public partial class Validator : IValidateOptions<ServerStartOptions>;
}
