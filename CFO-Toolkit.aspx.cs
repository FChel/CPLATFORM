using System;
using System.Configuration;
using System.Web;

/// <summary>
/// AWS landing page for the CFO Toolkit. The Defence Default.aspx remains a
/// separate page; WARATAH chooses this page through IIS default-document order.
/// Tool destinations and environment visibility remain configuration-driven.
/// </summary>
public partial class CFOToolkitPage : System.Web.UI.Page
{
    protected string EnvironmentLabel { get; private set; }
    protected string EnvironmentClass { get; private set; }

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.Cache.SetCacheability(HttpCacheability.NoCache);
        Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
        Response.Cache.SetNoStore();
        Response.AppendHeader("Pragma", "no-cache");
        ResolveEnvironment();
    }

    private void ResolveEnvironment()
    {
        string raw = ConfigurationManager.AppSettings["CPlatform.Environment"];
        if (String.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable("CPLATFORM_ENV");

        switch ((raw ?? String.Empty).Trim().ToUpperInvariant())
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

    protected bool IsTileVisible(string tileName)
    {
        string url = ConfigurationManager.AppSettings["CPlatform.Tile." + tileName + ".Url"];
        if (String.IsNullOrWhiteSpace(url)) return false;

        string environments = ConfigurationManager.AppSettings["CPlatform.Tile." + tileName + ".Environments"];
        if (String.IsNullOrWhiteSpace(environments)) return true;

        foreach (string environment in environments.Split(','))
        {
            if (String.Equals(environment.Trim(), EnvironmentLabel, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    protected string TileUrl(string tileName)
    {
        return ConfigurationManager.AppSettings["CPlatform.Tile." + tileName + ".Url"] ?? String.Empty;
    }

    protected string FirstTileUrl(string preferredTile, string fallbackTile)
    {
        if (IsTileVisible(preferredTile)) return TileUrl(preferredTile);
        return IsTileVisible(fallbackTile) ? TileUrl(fallbackTile) : String.Empty;
    }

    protected bool HasStatementsTile()
    {
        return IsTileVisible("NORMStatements") || IsTileVisible("NORM");
    }

    protected string HeroCtaUrl(string which)
    {
        return ConfigurationManager.AppSettings["CPlatform.HeroCta." + which + ".Url"] ?? String.Empty;
    }

    protected string HeroCtaLabel(string which)
    {
        return ConfigurationManager.AppSettings["CPlatform.HeroCta." + which + ".Label"] ?? String.Empty;
    }

    protected string PrimaryLaunchUrl()
    {
        return IsTileVisible("NORMWorkspace") ? TileUrl("NORMWorkspace") : HeroCtaUrl("Primary");
    }

    protected string PrimaryLaunchLabel()
    {
        return IsTileVisible("NORMWorkspace") ? "Open NORM workspace" : HeroCtaLabel("Primary");
    }
}
