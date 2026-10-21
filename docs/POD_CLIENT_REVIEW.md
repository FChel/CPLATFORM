# POD client review and deployment

This release prepares the POD screens for client UX review. The application uses the existing ASP.NET WebForms module and LPPI design system. Branding is POD; the internal `PORD/` URLs and settings remain compatible with earlier installations.

## Review session

1. Open `PORD/PORD_Admin.aspx`. Review the attention links, cycle progress, payment terms card and package list. Currency vs bank remains explicitly labelled **Example**; the other future checks are under **Upcoming checks**.
2. Open the ARMY review package. Try search, issue filters, contact filters, row details and the check tabs. Each PO × check is answered separately.
3. Choose **Amend PO terms** and enter a fix-by date. Try **Valid reason** with VR01: an Objective reference and comments are required. VR02 and VR99 also require comments; VR03 requires an Objective reference. **Not mine – reassign** needs comments naming the owner.
4. Save changes and inspect **Summary**. Changes remain unsaved until Save; edits made during a save stay pending. Try selecting several rows and applying a response before saving.
5. Open a PO contact link from **Send-outs → Open review**. The contact sees only their own POs and has no Finalise/Reopen action.
6. Finalise the AS Fin package using the in-page confirmation. Unanswered exceptions become **No response** and the review becomes read only. Reopen to make further changes. Accepted VR01–VR03 exclusions last 12 months for the current PO/check/value; VR99 does not exclude.
7. On **Send-outs**, preview AS Fin and every PO contact email. Email delivery is off; marking a package as sent/reminded records the workflow state only.
8. On **Load file**, choose **Check the sample file** or upload a CSV for header/classification checks. Unknown terms appear as rejected rows for ERP follow-up. Loading packages is disabled pending the agreed extract and approved persistence.
9. On **Outcomes**, download the outcomes CSV and DFIM list, inspect the resolved list, then close finalised packages. Downloading/closing does not email the reports.
10. For another session, use **Help → Restore sample data**, tick the confirmation and restore. This clears the shared sample responses and mailbox changes.

Use fictional data during this review. Sample changes are shared across viewers and reset when the application pool recycles. Store client feedback separately. The screens have no prototype/build banners or implementation notes; only a small Sample data indicator and operational notices about disabled functions.

## Server installation

Use the shared process in [WARATAH deployment](WARATAH_DEPLOYMENT.md). Preparing a local package does not deploy it. When publishing is authorised: open a PR into `uat`, wait for **WARATAH validation**, merge, then verify the actual **Deploy WARATAH** run and authenticated POD access. Report the deployment run URL and result.

Build a verified package from the committed revision:

```powershell
./tools/Test-Pord.ps1
./tools/deploy/New-WaratahPackage.ps1 -Commit HEAD -OutputDirectory .deployment/pod-client-review -Compile
./tools/deploy/Test-DeploymentSafety.ps1 -PackageInfo .deployment/pod-client-review/package.json
./tools/deploy/Test-DeploymentRequest.ps1
```

The package contains the supported application tree and manifest. Keep server `web.config`, UDLs, uploads and evidence intact. There is no POD SQL migration in this release. The WARATAH server's administrator-installed deployment tooling must already permit `PORD/`; see the existing module deployment note if it does not.

To show the landing tile, the site administrator can add these keys to the **existing** server configuration, choosing the permitted environments for that site:

```xml
<add key="CPlatform.Tile.PORD.Url" value="PORD/PORD_Admin.aspx" />
<add key="CPlatform.Tile.PORD.Environments" value="UAT" />
```

`PORD.DemoMode` defaults to true without configuration changes. Do not set it to false until an approved SQL store is implemented. The existing `LPPI.SapBaseUrl` controls ERP links; use the site's approved ERP host.

## WARATAH admin verification

POD keeps the LPPI admin allow-list; this release does not bypass it. The outstanding `WARATAH\Fred` request requires an authenticated server/database check. Do not assume `C:\Sites\CPLATFORM\Database\CPlatform.udl` exists: inspect the actual IIS site's physical path and effective `LPPI.ConnectionString` setting first. An explicit setting takes precedence; otherwise LPPI resolves `~/Database/CPlatform.udl` from that application's physical root.

Using the authorised connection, inspect `tblLPPI_AdminUsers` for `WARATAH\Fred` and `IsActive = 1`. If a change is required, use the guarded pattern in `sql/LPPI_AdminAdd.sql` with the intended identity, or an already-authorised LPPI administrator can use the existing Admin users page. Review an inactive existing row separately because the seed script only skips it. Follow the SQL approval process; never run the placeholder script unedited, execute every SQL file, or change the deployment baseline merely to pass validation. Verify the POD dashboard while signed in as that identity afterwards.

No server access, admin-account change or deployment is implied by a successful local UX test. The local browser verification uses an isolated identity replacement; the deployment package contains the real LPPI access check only.

## Later integration

The next release needs the first BODS extract, confirmed term mappings, the approved `tblPORD_*` schema/migrations, `PordSqlStore`, load/round reconciliation and SMTP delivery behind a production flag. Those changes must follow the existing SQL and deployment approval process. The current UI can be reviewed without them.
