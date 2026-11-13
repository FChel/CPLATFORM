using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Web;
using System.Web.Hosting;
using System.Web.SessionState;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Collections.Generic;

namespace CPlatform.NORM
{
    public partial class NORM_Import
    {
        sealed class UploadRequest : SimpleWorkerRequest
        {
            readonly byte[] body;
            const string Boundary = "NORMControlledMultipartBoundary";
            public UploadRequest(byte[][] files, string[] names) : base("/", Path.GetTempPath(), "NORM_Import.aspx", "", new StringWriter())
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        byte[] header = Encoding.ASCII.GetBytes("--" + Boundary + "\r\nContent-Disposition: form-data; name=\"TrialBalanceFile\"; filename=\"" + names[i] + "\"\r\nContent-Type: application/octet-stream\r\n\r\n");
                        stream.Write(header, 0, header.Length); stream.Write(files[i], 0, files[i].Length);
                        stream.WriteByte(13); stream.WriteByte(10);
                    }
                    byte[] end = Encoding.ASCII.GetBytes("--" + Boundary + "--\r\n"); stream.Write(end, 0, end.Length);
                    body = stream.ToArray();
                }
            }
            public override string GetHttpVerbName() { return "POST"; }
            public override string GetKnownRequestHeader(int index)
            {
                if (index == HeaderContentType) { return "multipart/form-data; boundary=" + Boundary; }
                if (index == HeaderContentLength) { return body.Length.ToString(); }
                return base.GetKnownRequestHeader(index);
            }
            public override byte[] GetPreloadedEntityBody() { return body; }
            public override bool IsEntireEntityBodyIsPreloaded() { return true; }
            public override int GetTotalEntityBodyLength() { return body.Length; }
        }

        public void InspectUploadsForTest(int primary, int[] additional, byte[][] files, string[] names)
        {
            HttpContext context = new HttpContext(new UploadRequest(files, names));
            SessionStateUtility.AddHttpSessionStateToContext(context, new HttpSessionStateContainer("isolated-session", new SessionStateItemCollection(),
                new HttpStaticObjectsCollection(), 20, true, HttpCookieMode.UseCookies, SessionStateMode.InProc, false));
            HttpContext.Current = context;
            typeof(Page).GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, context);
            typeof(Page).GetField("_requestValueCollection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, context.Request.Form);
            TrialBalanceFile.ID = "TrialBalanceFile";
            Controls.Add(TrialBalanceFile);
            BindReleases();
            ReleaseList.SelectedValue = primary.ToString();
            foreach (int id in additional) { AdditionalReleaseList.Items.FindByValue(id.ToString()).Selected = true; }
            ImportButton_Click(this, EventArgs.Empty);
        }
        public List<NORMImportPreview> PreviewsForTest { get { return Session[PendingKey] as List<NORMImportPreview>; } }
        public bool ConfirmationEnabledForTest { get { return ConfirmButton.Enabled; } }
        public string DefaultReleaseSelectionForTest() { BindReleases(); return ReleaseList.SelectedValue; }
        public void ChangeAdditionalForTest(int id)
        {
            ListItem item = AdditionalReleaseList.Items.FindByValue(id.ToString());
            if (item == null) { item = new ListItem("Controlled release", id.ToString()); AdditionalReleaseList.Items.Add(item); }
            item.Selected = !item.Selected;
        }
        public void ClearUploadedFilesForTest()
        {
            // Simulate the next postback: a fresh FileUpload has no uploaded bytes.
            Controls.Remove(TrialBalanceFile);
            TrialBalanceFile = new FileUpload();
        }
    }
}
