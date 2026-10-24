#!/usr/bin/env bash
# Installs a git pre-commit hook that scans staged changes with gitleaks.
# Refuses when gitleaks is absent or when a different pre-commit hook already exists.
# It never installs gitleaks itself. CI uses the same version (see .github/workflows/ci.yml).
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
hook="${repo_root}/.git/hooks/pre-commit"
marker="Installed by scripts/setup-hooks.sh"

if ! command -v gitleaks >/dev/null 2>&1; then
  echo "gitleaks is not on PATH; the hook was not installed." >&2
  echo "Install v8.21.2 from https://github.com/gitleaks/gitleaks/releases and run this script again." >&2
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
