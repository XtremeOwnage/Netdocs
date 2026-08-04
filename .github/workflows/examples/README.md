# Imported Docs Plugin - Examples

This directory contains examples for using the Imported Docs plugin with both push-based and pull-based approaches.

## Files

### Workflows
- **push-docs-workflow.yml** - For external repositories: workflow that pushes docs to main docs site
- **pull-external-docs-workflow.yml** - For main docs site: workflow that pulls from external repos

### Configuration
- **appsettings.imported-docs.example.json** - Example configuration showing both push and pull sources

## Quick Start

### For External Repositories (Push-Based)

If you have documentation in an external repository that you want to push to the main docs site:

1. Copy `push-docs-workflow.yml` to `.github/workflows/push-docs.yml` in your repository
2. Update the `MAIN_DOCS_REPO` and `DOCS_DESTINATION` environment variables
3. Ensure your repository has a `docs/` directory with markdown files
4. Commit and push — the workflow will trigger on changes to the docs directory

### For Main Documentation Site (Pull-Based)

If you maintain the main documentation site and want to pull from external repositories:

1. Copy `pull-external-docs-workflow.yml` to `.github/workflows/pull-external-docs.yml` 
2. Copy relevant parts of `appsettings.imported-docs.example.json` to your `appsettings.json`
3. Configure `pullSources` with your external repositories
4. For private repos, ensure `GITHUB_TOKEN` or other auth secrets are configured
5. Commit — the workflow will run on schedule and manual trigger

## Configuration Reference

See the full plugin documentation at `docs/plugins/imported-docs.md` for:
- All configuration options
- Push and pull approaches
- Authentication methods
- Front-matter overrides
- File exclusion patterns
- Troubleshooting

## Push vs Pull

**Push-Based**: External repo → Action → Pushes to main docs `/imported/` directory
- ✅ External repo owns when updates happen
- ✅ Simple setup for external contributors
- ✅ No need for external repo to grant access
- ❌ Requires push access to main docs repo

**Pull-Based**: Main docs repo ← Action ← Pulls from external repo
- ✅ Main docs repo controls update schedule
- ✅ Centralized configuration
- ✅ No changes needed in external repos
- ❌ Requires read access to external repos

Most organizations use **both** — some projects push, others are pulled on schedule.

## Notes

- Update repository URLs and paths in the examples to match your setup
- For private repositories, ensure proper authentication secrets are configured
- GitHub Actions uses `GITHUB_TOKEN` by default for public repos
- For enterprise/on-premises GitHub, you may need to update URLs
- Review the workflow files for comments on customization points
