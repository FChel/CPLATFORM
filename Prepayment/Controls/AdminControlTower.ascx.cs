using System;
using System.Web.UI;
using Prepayment.Web.Services;

namespace Prepayment.Web.Controls
{
    /// <summary>
    /// Admin Control Tower user control (Tab 4). Loads KPIs, the process tracker,
    /// exceptions, and the period summary from the DB via PPMAdminService.
    /// </summary>
    public partial class PPMAdminControlTower : UserControl
    {
        private readonly PPMAdminService _service = new PPMAdminService();

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadAndBind();
            }
        }

        private void LoadAndBind()
        {
            var vm = _service.Build();

            litPeriodLabel.Text    = Server.HtmlEncode(vm.PeriodLabel);
            litExceptionCount.Text = vm.ExceptionCount.ToString();

            rptKpis.DataSource          = vm.Kpis;
            rptProcessTracker.DataSource = vm.ProcessTracker;
            rptExceptions.DataSource    = vm.Exceptions;
            rptPeriodSummary.DataSource = vm.PeriodSummary;

            // §3.4 Admin-action pickers (data-driven).
            rptStuckItems.DataSource     = vm.StuckItems;
            rptApprovers.DataSource      = vm.Approvers;
            rptFailedBatches.DataSource  = vm.FailedBatches;
            rptOpenExceptions.DataSource = vm.OpenExceptions;

            phNoStuck.Visible    = vm.StuckItems.Count == 0;
            phNoBatches.Visible  = vm.FailedBatches.Count == 0;
            phNoClearable.Visible = vm.OpenExceptions.Count == 0;

            phNoExceptions.Visible   = vm.Exceptions.Count == 0;
            phNoTrackerRows.Visible  = vm.ProcessTracker.Count == 0;

            DataBind();
        }
    }
}
