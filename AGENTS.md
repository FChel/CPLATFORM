# Shared development and WARATAH publishing

- `uat` is the shared application integration and WARATAH deployment branch. Start new application work from the latest `origin/uat`; `main` retains an older application tree and registers the workflows.
- WARATAH publishing is already configured through GitHub Actions, temporary AWS OIDC credentials, a private S3 package bucket and Systems Manager. Neither developer nor a browser/cloud coding session needs personal AWS credentials.
- `.github/workflows/deploy-waratah.yml` deploys pushes/merges to `uat`. The `waratah` environment accepts only `uat`; feature-branch commits do not deploy by themselves.
- If an older feature branch lacks the workflow or deployment scripts, fetch `origin/uat` and integrate its deployment setup without discarding the feature work. Do not report that the AWS connection is missing solely because the current branch lacks those files or local AWS credentials have expired.
- When publishing is requested or already authorised, open a pull request into `uat`, wait for `WARATAH validation`, merge it, and verify the resulting `Deploy WARATAH` run. Report the run URL and actual outcome. A source commit alone is not a completed deployment.
- Keep existing server `web.config`, UDL connections, uploads and evidence intact. SQL changes require the explicit baseline/migration process; never bypass that check or execute all SQL files automatically.
- See [the shared deployment guide](docs/WARATAH_DEPLOYMENT.md) for operating instructions, initial setup records and rollback.
