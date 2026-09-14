# Shared WARATAH deployment

Both developers publish through **FChel/CPLATFORM → GitHub Actions → AWS OIDC → S3 → Systems Manager → IIS**. Developers need repository write access; they do not need individual AWS credentials or a local Codex session. A browser-created change follows the same pull request and deployment process as a local change.

## Target verified on 14 September 2026

| Setting | Value |
| --- | --- |
| AWS account / region | `663358704059` / `us-east-1` |
| WARATAH instance | `i-0b35e9aea70f49701` (SSM Online) |
| IIS site / application pool | `CPLATFORM` / `CPLATFORM` |
| Application folder | `C:\Sites\CPLATFORM` |
| Internal health URL | `http://localhost:8082/` |
| Browser URL | <https://cplatform.beeqira.com.au/> |
| Deployment branch / environment | `uat` / `waratah` |
| Deployment role | `arn:aws:iam::663358704059:role/GitHub-CPLATFORM-WARATAH` |
| Private bucket | `cplatform-waratah-deploy-663358704059-us-east-1` |
| Approved SSM document | `CPLATFORM-Deploy-Waratah-v1`, pinned version `1` |

The repository reports default OIDC subjects without immutable IDs: `repo:FChel/CPLATFORM:environment:waratah`. Recheck `GET /repos/FChel/CPLATFORM/actions/oidc/customization/sub` before changing AWS trust. The environment permits only the `uat` branch, not tags. Repository variable `WARATAH_AUTO_DEPLOY` controls automatic publishing and is enabled following the successful initial manual deployment.

**Activated on 14 September 2026.** The AWS administrator ran the prepared infrastructure script in CloudShell. The verified server tools were installed through SSM, with a read-only database preflight confirming 66 required objects, 23 migration-related columns and the expected written disclosures. No SQL was executed. The first successful manual release deployed commit `aaf745e108a7aac6c94f99adbe0c2c6526f879fd`: [GitHub deployment run](https://github.com/FChel/CPLATFORM/actions/runs/34821442592), SSM command `aa5c173e-8c63-4e13-ae6a-6085c9b3b307`. All configured HTTP checks passed before and after deployment, and the previous files were backed up under `C:\ProgramData\CPLATFORM-Deploy\runs\34821442592-1\backup`.

The server originally had the older LPPI reviewer instructions from commit `9642eee`; the initial release brought those two files up to the existing FAQ and spacing changes in `uat`. All other runtime content already matched `uat` after accounting for line endings. Reporting and Mapping still require an authenticated application user for acceptance testing; deployment does not change those access controls.

## One-time administrator setup

1. Review and run `tools/deploy/Set-WaratahInfrastructure.ps1` from the repository using an AWS administrator session. It checks the account, creates/reuses the GitHub provider, creates the dedicated bucket and role, grants the instance role download access, and creates the fixed deployment command. If AWS CLI is not on PATH, use `-Aws <path-to-aws.exe>`. It never modifies the administrator's own access policy.
   AWS CloudShell includes `pwsh`, so an administrator can run this script using their console session without creating access keys. Fetch the script from a reviewed, immutable repository commit before running it.
2. The administrator needs IAM provider/role creation and policy management, S3 bucket creation/configuration, and SSM document creation, plus the corresponding read/list permissions used by the script. Existing instance role: `PUKARA-Dev-IIS-SQL-InstanceRole`. No EC2 instance replacement or IIS reconfiguration is required.
3. Run `tools/deploy/Set-WaratahGitHub.ps1` if recreating the GitHub environment. It uses `GH_TOKEN`, `GITHUB_TOKEN`, or the existing Git credential in memory. It adds `AWS_REGION`, `AWS_INSTANCE_ID`, `AWS_ROLE_ARN`, `DEPLOY_BUCKET`, and `SSM_DOCUMENT_NAME` as environment variables. It creates the repository automation toggle as `false` if missing. Never put access keys in the repository, Codex cloud, or GitHub secrets for this workflow.
4. On WARATAH, copy `tools/deploy` to an administrator working folder and run `Install-WaratahServer.ps1` in **elevated Windows PowerShell 5.1**. AWS Tools for PowerShell (`Read-S3Object`) is already available. Supply `-SqlSha256` and `-DatabaseApproval` after checking the schema as described below. This installs the trusted scripts and target config under `C:\ProgramData\CPLATFORM-Deploy`, accessible to SYSTEM and Administrators. It runs health checks but does not change application files or execute SQL.
   An administrator can also run the installer through `AWS-RunPowerShellScript`, as used for the initial installation. Download only the installer, target config and three runtime scripts from a pinned commit, verify their SHA-256 hashes, then invoke the installer. GitHub raw downloads use LF line endings; use `git -c core.autocrlf=false archive` when deriving their reference hashes on Windows. Do not grant the GitHub deployment role permission to bootstrap or replace these administrator-installed scripts.
5. The workflow file must also exist on GitHub's default branch for the browser's **Run workflow** control. Register deployment tooling there without replacing its application code. Deployments themselves use the exact commit selected from `uat`; `main` is rejected by the job condition and environment policy.
6. Open **Actions → Deploy WARATAH → Run workflow**, select **uat**, and run once. Confirm compilation, temporary AWS authentication, package upload, SSM completion, and HTTP checks all succeed. The summary shows the commit, ZIP checksum and SSM command ID. Then test Reporting, Mapping and the changed feature while signed in as an authorised application user.
7. After that successful manual deployment and deployment smoke checks, change repository variable **WARATAH_AUTO_DEPLOY** to **true**. Future pushes/merges into `uat` deploy automatically, followed by the developers' authenticated feature checks. Set it back to `false` to pause automatic deployments; manual runs remain available.

Keep each developer's working branch based on `uat` once this shared flow is adopted. Open pull requests **into `uat`** and wait for **WARATAH validation** before merging. Do not base new UAT work on the older `main` application tree. Consider making this check required in the repository branch protection settings.

## SQL approval and tracking

Application publishing never runs SQL. `New-WaratahPackage.ps1` records a SHA-256 digest of the paths and contents of **all tracked `sql/*.sql` files**, including nested SQL files. Deployment compares it with the administrator's `database-baseline.json`. A mismatch fails before maintenance begins. The initial source digest at commit `a70e286e8725938cef476e46d77777c38979dbc4` is:

```text
41bc9f60c323fc0de53ca34a2197547f1461916583bb0b280c8e8067442c495c
```

This is a **source fingerprint, not evidence that SQL has been applied**. Before recording it, the database administrator must confirm the required schema/data changes are already installed. Never execute all SQL files in order automatically: this repository includes optional resets, data promotion, admin-user scripts and fresh-install scripts.

For each later SQL change, review the diff, back up the database, apply only the intended migration, verify it, and record the new digest and migration/change reference using the server installer. Example after approval:

```powershell
.\Install-WaratahServer.ps1 -SqlSha256 '<approved 64-character digest>' -DatabaseApproval 'Change reference; scripts applied or reviewed; database verification result'
```

The installer appends the approval, Windows identity and UTC timestamp to `database-history.jsonl`, then updates the baseline. Do not update the digest just to bypass a deployment failure. Database rollback is a separate database operation.

## What a deployment changes

Packaging uses `git archive` for the resolved commit, independent of untracked files and local working changes. Only the supported application directories and runtime extensions enter the ZIP. A manifest identifies the commit and every file checksum. ASP.NET 4.8 precompilation validates pages and C# with a build-only config; the package contains source files for the existing WebForms dynamic compilation model.

The server downloads from the configured private bucket, validates the ZIP checksum, paths, expanded size, manifest, per-file hashes, approved SQL digest, IIS target, and baseline HTTP checks. It rejects ZIP traversal, duplicate paths, reparse points, protected paths and unexpected payload files.

A server lock and GitHub concurrency group prevent overlapping deployments. The script creates an external transaction record, places `app_offline.htm`, stops only the CPLATFORM pool, backs up affected files, copies the complete application payload and checks deployed hashes. Files removed from a previous **managed manifest** are removed. Unknown server files are retained, including on the first deployment; review obsolete server code separately if adopting an existing installation.

Every `web.config`, UDL, `Database`, `App_Data`, upload/evidence directory and IIS configuration remains server-owned. SQL, source documents, delivery ZIPs, local tools and deployment scripts are excluded from the website package. Tools execute from the administrator-installed directory, not the downloaded release.

The pool restarts and health checks require HTTP 200 and matching content on the homepage, stylesheet and NORM Financial Statements page. Reporting and Mapping returned 403 under the SSM SYSTEM identity during inspection; they require manual authenticated acceptance testing. The automated checks do not certify every application action or database operation.

SSM command permissions are restricted to the named document and instance. The role cannot invoke arbitrary `AWS-RunPowerShellScript`, update the installed tooling, download packages, change IAM, or change IIS configuration. The instance role has read access to this bucket. Packages have S3 encryption, blocked public access, TLS enforcement, versioning, and 90-day release expiry (30 days for noncurrent versions).

## Failure, rollback and recovery

A copy or post-deployment check failure restores backed-up files, removes newly introduced files, starts the pool and verifies the old application. The workflow still fails. Backups and transaction records remain under `C:\ProgramData\CPLATFORM-Deploy\runs\<run-id>-<attempt>`. A successful deployment records `current.json`. Do not delete a pending transaction or its backup to force another deployment.

To roll back a completed release, **re-run all jobs on the desired earlier successful manual deployment** in GitHub Actions. Re-runs retain the original commit and get a new attempt ID. This works only while that source is compatible with the approved SQL baseline. Check the resulting commit and test affected pages. Application rollback does not revert database changes.

If the process or machine dies mid-deployment, `pending.json` blocks subsequent runs. An administrator should take the deployment lock, inspect its `phase` and records, and recover the recorded files before clearing it. A `stopping` phase means application copying has not started; a `copying` phase means the complete backup exists and must be restored. Restore only recorded paths whose `existed` flag is true; remove only recorded new application files; leave configuration and data untouched. Restore the recorded `previous` manifest, remove the maintenance file, start the CPLATFORM pool, run `Test-Waratah.ps1` with the installed config, and archive the transaction after successful recovery.

If GitHub stops waiting or a runner is cancelled, inspect the recorded SSM command; cancellation does not prove the server command stopped. The server lock prevents concurrent mutation. GitHub concurrency does not cancel a running deployment. WARATAH must be running and SSM Online; the workflow does not start a stopped EC2 instance.

Backups contain application files and remain on the server for administrator-controlled retention. Monitor free space and archive old successful run directories outside the application folder when no longer needed. Never delete the current or pending run backup.

## Validation

```powershell
.\tools\deploy\New-WaratahPackage.ps1 -Commit HEAD -OutputDirectory .deployment\validation -Compile
.\tools\deploy\Test-DeploymentSafety.ps1 -PackageInfo .deployment\validation\package.json
.\tools\deploy\Test-DeploymentRequest.ps1
```

The safety test substitutes fake IIS, S3 and HTTP checks in a temporary directory. It exercises successful deployment, forced post-copy failure and rollback, stale managed-file removal, preservation of config/UDL/evidence/unknown files, traversal rejection, ZIP checksum failure, and SQL mismatch. The request test verifies BOM-free JSON for the AWS CLI in PowerShell 5.1, exact SSM parameters, completion polling and rejection of credentials for the wrong AWS account before upload. Neither test contacts AWS or live IIS. The separate validation workflow also runs on pull requests into `uat`.

References: [GitHub AWS OIDC](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-aws), [deployment environments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments), [AWS Run Command setup](https://docs.aws.amazon.com/systems-manager/latest/userguide/run-command-setting-up.html), [SSM document parameter handling](https://docs.aws.amazon.com/systems-manager/latest/userguide/documents-schemas-features.html).
