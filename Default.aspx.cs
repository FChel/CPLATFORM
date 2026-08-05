using System;
using System.Configuration;
using System.Web;

/// <summary>
/// CPLATFORM landing / intro page (FinHub).
///
/// Environment label (DEV / UAT / PROD / UNKNOWN) is resolved from a single,
/// required source:
///   1. web.config appSetting "CPlatform.Environment"  (required — explicit)
///   2. Environment variable  "CPLATFORM_ENV"           (fallback for environments
///                                                       where editing web.config is
///                                                       impractical)
///
/// If neither is set, or if the value does not match DEV/UAT/PROD exactly
/// (case-insensitive), the chip shows "UNKNOWN" in red so misconfiguration is
/// immediately visible. IIS site name and machine name are NOT used — they are
/// unreliable heuristics that can silently show "PROD" on the wrong box.
///
/// Tile and hero CTA visibility:
///   Each tile is driven by two appSettings:
///     CPlatform.Tile.<n>.Url          — URL for this environment (required; empty = tile hidden)
///     CPlatform.Tile.<n>.Environments — comma-separated environments to show the tile in;
///                                        absent/blank = show in all environments
///   IsTileVisible() and TileUrl() are called from Default.aspx markup.
///   HeroCtaUrl() and HeroCtaLabel() expose the hero button values.
/// </summary>
public partial class CPlatformPage : System.Web.UI.Page
{
    protected string EnvironmentLabel { get; private set; }
    protected string EnvironmentClass { get; private set; }

    protected void Page_Load(object sender, EventArgs e)
    {
        // Cache prevention so the banner always reflects the current environment.
        Response.Cache.SetCacheability(HttpCacheability.NoCache);
        Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
        Response.Cache.SetNoStore();
        Response.AppendHeader("Pragma", "no-cache");

        ResolveEnvironment();
    }

    // -----------------------------------------------------------------------
    // Environment resolution
    // -----------------------------------------------------------------------

    private void ResolveEnvironment()
    {
        string raw = ConfigurationManager.AppSettings["CPlatform.Environment"];

        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable("CPLATFORM_ENV");

        switch ((raw ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "DEV":
                EnvironmentLabel = "DEV";
                EnvironmentClass = "env-dev";
                break;
            case "UAT":
                EnvironmentLabel = "UAT";
                EnvironmentClass = "env-uat";
                break;
            case "PROD":
                EnvironmentLabel = "PROD";
                EnvironmentClass = "env-prod";
                break;
            default:
                EnvironmentLabel = "UNKNOWN";
                EnvironmentClass = "env-unknown";
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Tile helpers — called from Default.aspx markup
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns true when the named tile should be rendered for the current environment.
    /// Hidden when: Url key is absent/blank, OR Environments key is non-blank and does
    /// not include the current environment.
    /// </summary>
    protected bool IsTileVisible(string tileName)
    {
        string url = ConfigurationManager.AppSettings["CPlatform.Tile." + tileName + ".Url"];
        if (string.IsNullOrWhiteSpace(url))
            return false;

        string envList = ConfigurationManager.AppSettings["CPlatform.Tile." + tileName + ".Environments"];
        if (string.IsNullOrWhiteSpace(envList))
            return true; // no restriction — show in all environments

        foreach (string env in envList.Split(','))
        {
            if (string.Equals(env.Trim(), EnvironmentLabel, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Returns the URL for the named tile as configured for this environment.</summary>
    protected string TileUrl(string tileName)
    {
        return ConfigurationManager.AppSettings["CPlatform.Tile." + tileName + ".Url"] ?? string.Empty;
    }

    // -----------------------------------------------------------------------
    // Hero CTA helpers
    // -----------------------------------------------------------------------

    protected string HeroCtaUrl(string which)
    {
        return ConfigurationManager.AppSettings["CPlatform.HeroCta." + which + ".Url"] ?? string.Empty;
    }

    protected string HeroCtaLabel(string which)
    {
        return ConfigurationManager.AppSettings["CPlatform.HeroCta." + which + ".Label"] ?? string.Empty;
    }
}
