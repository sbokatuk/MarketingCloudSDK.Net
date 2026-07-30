#!/bin/sh
# Re-pins the packages this façade builds against, in Directory.Build.props, and then reports
# exactly which README lines still disagree.
#
# This is the "mechanical part" the upstream-drift issue template points at. It deliberately does
# NOT touch README.md: the README states the versions in several shapes (install pins, the version
# map row, prose mentions), CheckReadmeVersions.sh is the one thing that knows all of them, and a
# sed sweep across a document that also explains the version *scheme* is how you get a README that
# passes its own check while saying something false. So: props are rewritten here, the README is
# reported and edited by hand, and CheckReadmeVersions.sh remains the gate.
#
# Usage:
#   ./build/RepinPackages.sh [--android <version>] [--ios <version>] [--core <version>]
#                            [--native <version>] [--android-native <version>] [--revision <n>]
#
# Examples:
#   ./build/RepinPackages.sh --ios 11.0.2.3 --core 4.0.1.2      # binding revisions moved
#   ./build/RepinPackages.sh --native 11.0.3 --revision 1       # the iOS native line moved
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
props="$root/Directory.Build.props"

android=''
ios=''
core=''
native=''
android_native=''
revision=''

usage() {
  sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --android)        android="${2:?--android needs a version}" ; shift 2 ;;
    --ios)            ios="${2:?--ios needs a version}" ; shift 2 ;;
    --core)           core="${2:?--core needs a version}" ; shift 2 ;;
    --native)         native="${2:?--native needs a version}" ; shift 2 ;;
    --android-native) android_native="${2:?--android-native needs a version}" ; shift 2 ;;
    --revision)       revision="${2:?--revision needs a number}" ; shift 2 ;;
    -h|--help)        usage 0 ;;
    *) printf 'Unknown argument: %s\n\n' "$1" >&2 ; usage 1 >&2 ;;
  esac
done

if [ -z "$android$ios$core$native$android_native$revision" ]; then
  printf 'Nothing to do: pass at least one of --android/--ios/--core/--native/--android-native/--revision.\n\n' >&2
  usage 1 >&2
fi

prop() {
  sed -n "s/.*<$1>\(.*\)<\/$1>.*/\1/p" "$props" | head -1
}

# One property per invocation, matched on its own line - the same single-line shape prop() above and
# CheckReadmeVersions.sh, pr.yml, release.yml and the package tests all read by name. A rewrite that
# reflowed these lines would break every one of those readers silently.
repin() {
  name="$1"
  value="$2"
  current="$(prop "$name")"

  if [ -z "$current" ]; then
    printf 'error: <%s> not found in Directory.Build.props.\n' "$name" >&2
    exit 1
  fi

  if [ "$current" = "$value" ]; then
    printf '  %-28s %s (unchanged)\n' "$name" "$value"
    return 0
  fi

  tmp="$(mktemp)"
  sed "s|<$name>$current</$name>|<$name>$value</$name>|" "$props" > "$tmp"
  mv "$tmp" "$props"
  printf '  %-28s %s -> %s\n' "$name" "$current" "$value"
}

printf 'Re-pinning %s:\n' "${props#"$root"/}"
[ -n "$android" ]        && repin SfmcAndroidPackageVersion "$android"
[ -n "$ios" ]            && repin SfmcIosPackageVersion "$ios"
[ -n "$core" ]           && repin SfmcCorePackageVersion "$core"
[ -n "$native" ]         && repin SfmcNativeVersion "$native"
[ -n "$android_native" ] && repin SfmcAndroidNativeVersion "$android_native"
[ -n "$revision" ]       && repin SfmcBindingRevision "$revision"

printf '\nChecking README.md against the new pins:\n'
if "$root/build/CheckReadmeVersions.sh"; then
  printf '\nREADME.md already agrees. Next: add docs/release-notes/%s.%s.md and open a PR.\n' \
    "$(prop SfmcNativeVersion)" "$(prop SfmcBindingRevision)"
else
  printf '\nEdit the README lines listed above, then re-run ./build/CheckReadmeVersions.sh.\n' >&2
  printf 'A native bump usually also wants SfmcBindingRevision reset to 1 (--revision 1).\n' >&2
  exit 1
fi
