using System.Collections.Generic;

namespace Prepayment.Web.Services.ExcelImport
{
    /// <summary>
    /// net48 doesn't have IDictionary.GetValueOrDefault (that's .NET Core+), so provide it here
    /// for the row dictionaries the Excel parser builds.
    /// </summary>
    internal static class PPMDictExtensions
    {
        public static object GetValueOrDefault(this Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }
    }
}
