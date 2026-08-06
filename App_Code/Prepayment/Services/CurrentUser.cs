using System;
using System.Linq;
using System.Web;
using System.Web.Caching;
using Prepayment.Web.DataAccess;

namespace Prepayment.Web.Services
{
    /// <summary>
    /// Resolves the authenticated Windows account to a prepayment.AppUser.Id for stamping
    /// CreatedBy / ModifiedBy on writes. Looks the account up from prepayment.AppUser by
    /// WindowsAccount (active, non-deleted rows only, matching the lookup convention used
    /// elsewhere against this table) and caches the result per account. Centralised here so
    /// every handler resolves the user the same way.
    /// </summary>
    public static class PPMCurrentUser
    {
        private const string CacheKeyPrefix = "PPMCurrentUser.Id.";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        public static int ResolveId(HttpContext context)
        {
            string account = context.User != null && context.User.Identity != null
                ? context.User.Identity.Name
                : null;

            if (string.IsNullOrEmpty(account))
            {
                throw new InvalidOperationException("No authenticated Windows account on the current request.");
            }

            string cacheKey = CacheKeyPrefix + account;
            object cached = HttpRuntime.Cache[cacheKey];
            if (cached is int)
            {
                return (int)cached;
            }

            int id = LookupId(account);

            HttpRuntime.Cache.Insert(cacheKey, id, null, DateTime.UtcNow.Add(CacheDuration), Cache.NoSlidingExpiration);

            return id;
        }

        public static string ResolveRole(HttpContext context)
        {
            int userId = ResolveId(context);
            string role = PPMDbHelper.QuerySingleOrDefaultText(
                "SELECT RoleName FROM dbo.tblPPM_AppUser WHERE Id = ? AND IsDeleted = 0 AND IsActive = 1",
                record => record.IsDBNull(0) ? null : Convert.ToString(record.GetValue(0)),
                userId);

            if (string.IsNullOrEmpty(role))
                throw new InvalidOperationException("The current prepayment user does not have an active role.");

            return role;
        }

        public static int RequireRole(HttpContext context, params string[] allowedRoles)
        {
            string role = ResolveRole(context);
            if (allowedRoles == null || !allowedRoles.Any(r =>
                    string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException(
                    "Your prepayment role does not permit this administrative action.");
            }

            return ResolveId(context);
        }

        private static int LookupId(string windowsAccount)
        {
            int? id = PPMDbHelper.QuerySingleOrDefaultText(
                "SELECT Id FROM dbo.tblPPM_AppUser WHERE WindowsAccount = ? AND IsDeleted = 0 AND IsActive = 1",
                record => record.IsDBNull(0) ? (int?)null : System.Convert.ToInt32(record.GetValue(0)),
                windowsAccount);

            if (id == null)
            {
                throw new InvalidOperationException(
                    "No active dbo.tblPPM_AppUser found for Windows account '" + windowsAccount + "'.");
            }

            return id.Value;
        }
    }
}
