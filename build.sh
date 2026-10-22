#!/bin/zsh
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p .build/ModuleCache dist
task_stage="$(mktemp -d .build/app.XXXXXX)"
trap 'rm -rf "$task_stage"' EXIT
mkdir -p "$task_stage/Campus Quota.app/Contents/MacOS"
task_architectures="${CAMPUSQUOTA_ARCHS:-$(uname -m)}"
for task_arch in ${=task_architectures}; do
  case "$task_arch" in arm64|x86_64) ;; *) echo "Unsupported architecture: $task_arch" >&2; exit 1 ;; esac
  xcrun swiftc -module-cache-path .build/ModuleCache -O -target "${task_arch}-apple-macos14.0" -framework AppKit -framework WebKit -framework Security -framework Network Sources/CampusQuota/*.swift -o "$task_stage/CampusQuota-$task_arch"
done
xcrun lipo -create "$task_stage"/CampusQuota-* -output "$task_stage/Campus Quota.app/Contents/MacOS/CampusQuota"
cp Info.plist "$task_stage/Campus Quota.app/Contents/Info.plist"
codesign --force --sign - "$task_stage/Campus Quota.app"
"$task_stage/Campus Quota.app/Contents/MacOS/CampusQuota" --self-test
codesign --verify --deep --strict "$task_stage/Campus Quota.app"
# Rename bundles rather than overwriting the executable of a running version.
if [[ -d "dist/Campus Quota.app" ]]; then
  mv "dist/Campus Quota.app" "$task_stage/Previous.app"
fi
mv "$task_stage/Campus Quota.app" "dist/Campus Quota.app"
echo "Built dist/Campus Quota.app"
