using System.Collections.Generic;

namespace Prepayment.Web.Models.Entities
{
    /// <summary>A single filter dropdown option: the value sent to the SP + the display label.</summary>
    public class PPMGroupFilterOption
    {
        public string Key   { get; set; }
        public string Label { get; set; }
    }

    /// <summary>The data-driven option lists for the three Group Workflow filter dropdowns.</summary>
    public class PPMGroupFilterOptions
    {
        public List<PPMGroupFilterOption> Statuses   { get; set; }
        public List<PPMGroupFilterOption> GroupNames { get; set; }
        public List<PPMGroupFilterOption> Preparers  { get; set; }

        public PPMGroupFilterOptions()
        {
            Statuses = new List<PPMGroupFilterOption>();
            GroupNames = new List<PPMGroupFilterOption>();
            Preparers = new List<PPMGroupFilterOption>();
        }
    }

    /// <summary>Row shape for the Group_GetFilterOptions status result set.</summary>
    internal class PPMGroupStatusOptionRow { public string StatusKey { get; set; } public string StatusLabel { get; set; } public int SortOrder { get; set; } }
}
