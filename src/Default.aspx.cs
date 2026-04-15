using System;
using System.Configuration;
using System.Web;
using System.Web.Hosting;

/// <summary>
/// CPLATFORM landing / intro page.
///
/// Environment label (UAT / PROD / DEV / UNKNOWN) is resolved dynamically — never
/// hardcoded in the application source. Resolution order:
///   1. web.config appSetting "CPlatform.Environment"   (explicit override)
///   2. Environment variable      "CPLATFORM_ENV"        (set on the box / app pool)
///   3. IIS site name             (HostingEnvironment.SiteName)
///   4. Machine name fallback     (Environment.MachineName)
///
/// Matching rules applied to whichever value above is found first:
///   - contains "dev"  -> DEV
///   - contains "uat"  -> UAT
///   - otherwise, if the value looks like a real prod host (non-empty and
///     contains none of the non-prod markers like "test", "local", "stage"…)
///     -> PROD
///   - else -> UNKNOWN  (red chip, so misconfiguration is visible)
///
/// To switch the banner from UAT to PROD on the production box, set ONE of:
///   - the IIS site name to the production site name (no dev/uat/test in it)
///   - the CPLATFORM_ENV environment variable on the app pool
///   - a CPlatform.Environment appSetting in that environment's web.config
/// No code change or redeploy of the binaries is required.
/// </summary>
public partial class CPlatformPage : System.Web.UI.Page
{
    protected string EnvironmentLabel { get; private set; }
    protected string EnvironmentClass { get; private set; }
    protected string HostName       { get; private set; }
    protected string SiteName       { get; private set; }

    protected void Page_Load(object sender, EventArgs e)
    {
        // Cache prevention so the banner always reflects the current environment
        Response.Cache.SetCacheability(HttpCacheability.NoCache);
        Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
        Response.Cache.SetNoStore();
        Response.AppendHeader("Pragma", "no-cache");

        ResolveEnvironment();
    }

    private void ResolveEnvironment()
    {
        SiteName = SafeGet(() => HostingEnvironment.SiteName) ?? string.Empty;
        HostName = SafeGet(() => Environment.MachineName) ?? string.Empty;

        string raw = ConfigurationManager.AppSettings["CPlatform.Environment"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Environment.GetEnvironmentVariable("CPLATFORM_ENV");
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = SiteName;
        }
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = HostName;
        }

        string lower = (raw ?? string.Empty).ToLowerInvariant();

        if (lower.Contains("dev"))
        {
            EnvironmentLabel = "DEV";
            EnvironmentClass = "env-dev";
        }
        else if (lower.Contains("uat"))
        {
            EnvironmentLabel = "UAT";
            EnvironmentClass = "env-uat";
        }
        else if (LooksLikeProd(lower))
        {
            EnvironmentLabel = "PROD";
            EnvironmentClass = "env-prod";
        }
        else
        {
            EnvironmentLabel = "UNKNOWN";
            EnvironmentClass = "env-unknown";
        }
    }

    /// <summary>
    /// Returns true only if the value looks like a real production host:
    /// non-empty and containing none of the markers we associate with
    /// non-production environments. This prevents an empty site name or a
    /// developer's laptop from being silently displayed as PROD.
    /// </summary>
    private static bool LooksLikeProd(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] nonProdMarkers =
        {
            "dev", "uat", "test", "tst", "local", "localhost",
            "stage", "stg", "sandbox", "sbx"
        };

        foreach (string marker in nonProdMarkers)
        {
            if (value.Contains(marker)) return false;
        }
        return true;
    }

    private static string SafeGet(Func<string> f)
    {
        try { return f(); } catch { return null; }
    }
}