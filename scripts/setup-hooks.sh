#!/usr/bin/env bash
# Installs a git pre-commit hook that scans staged changes with gitleaks.
# Refuses when gitleaks is absent or is not version 8.21.2, or when a different pre-commit hook already exists.
# It never installs gitleaks itself. CI uses the same version (see .github/workflows/ci.yml).
set -euo pipefail

# Keep in step with GITLEAKS_VERSION in .github/workflows/ci.yml.
required_version="8.21.2"

repo_root="$(git rev-parse --show-toplevel)"
hook="${repo_root}/.git/hooks/pre-commit"
marker="Installed by scripts/setup-hooks.sh"

if ! command -v gitleaks >/dev/null 2>&1; then
  echo "gitleaks is not on PATH; the hook was not installed." >&2
  echo "Install v${required_version} from https://github.com/gitleaks/gitleaks/releases and run this script again." >&2
  exit 1
fi

# `gitleaks version` prints the bare number, with or without a leading v depending on the build.
found_version="$(gitleaks version 2>/dev/null | head -n 1 | tr -d 'v[:space:]')"
if [ "${found_version}" != "${required_version}" ]; then
  echo "gitleaks ${found_version:-of unknown version} is on PATH, but this repository uses ${required_version}; the hook was not installed." >&2
  echo "Install v${required_version} from https://github.com/gitleaks/gitleaks/releases and run this script again." >&2
  exit 1
fi

if [ -e "${hook}" ] && ! grep -qF "${marker}" "${hook}"; then
  echo "${hook} exists and was not installed by this script; refusing to overwrite it." >&2
  exit 1
fi

cat > "${hook}" <<HOOK
#!/usr/bin/env bash
# ${marker}: scan staged changes for secrets.
exec gitleaks protect --staged --redact --config .gitleaks.toml
HOOK
chmod +x "${hook}"
echo "Installed ${hook}"
