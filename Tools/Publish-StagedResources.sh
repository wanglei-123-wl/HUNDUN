#!/usr/bin/env bash
set -euo pipefail
release_id="${1:?Release ID required}"
manifest_hash="${2:?Manifest SHA256 required}"
previous_hash="${3:?Previous public catalog hash required}"
new_hash="${4:?New catalog hash required}"
checksums_hash="${5:?Checksum-list SHA256 required}"
[[ "$release_id" =~ ^[a-f0-9]{32}$ && "$manifest_hash" =~ ^[a-f0-9]{64}$ ]]
[[ "$previous_hash" =~ ^[a-f0-9]{32}$ && "$new_hash" =~ ^[a-f0-9]{32}$ ]]
[[ "$checksums_hash" =~ ^[a-f0-9]{64}$ ]]
exec 9>/srv/hundun-release-publish.lock
flock -n 9 || { printf 'Another resource publish is running.\n' >&2; exit 1; }
staging="/srv/hundun-upload/$release_id"
site='/srv/hundun-resources'
versions='/srv/hundun-releases'
version="$versions/$release_id"
active="$site/StandaloneWindows64"
checksums="$staging/SHA256SUMS"
stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
previous=''
changed=0
committed=0

switch_link() {
    local target="$1" temporary="$site/.StandaloneWindows64.$stamp.$RANDOM"
    ln -s -- "$target" "$temporary"
    mv -Tf -- "$temporary" "$active"
}
trap 'rc=$?; if [[ "$rc" -ne 0 && "$changed" -eq 1 && "$committed" -eq 0 && -n "$previous" ]]; then switch_link "$previous"; printf "ROLLED_BACK_TO=%s\n" "$previous" >&2; fi' EXIT

verify_directory() {
    local directory="$1"
    [[ -d "$directory" && ! -L "$directory" ]]
    [[ "$(realpath -m -- "$directory")" == "$directory" ]]
    [[ -z "$(find "$directory" -type l -print -quit)" ]]
    (cd "$directory" && sha256sum --strict -c "$checksums")
}

[[ "$(realpath -m -- "$staging")" == "$staging" ]]
[[ -f "$staging/release-manifest.json" && -f "$checksums" ]]
printf '%s  %s\n' "$manifest_hash" "$staging/release-manifest.json" | sha256sum --strict -c -
printf '%s  %s\n' "$checksums_hash" "$checksums" | sha256sum --strict -c -
current_hash="$(curl --noproxy '*' --fail --silent --show-error "http://127.0.0.1:18761/StandaloneWindows64/catalog_0.1.hash?before=$stamp")"
[[ "$current_hash" == "$previous_hash" || "$current_hash" == "$new_hash" ]] || {
    printf 'Live catalog changed since the client-side check; publishing stopped.\n' >&2; exit 1;
}
mkdir -p -- "$site" "$versions"
[[ "$(realpath -m -- "$versions")" == "$versions" ]]
if [[ -L "$active" ]]; then previous="$(readlink -f -- "$active")";
elif [[ -d "$active" ]]; then previous="$active";
else printf 'Existing live resources were not found.\n' >&2; exit 1; fi

if [[ -e "$version" ]]; then verify_directory "$version";
else verify_directory "$staging/RemotePayload"; cp -a -- "$staging/RemotePayload" "$version"; fi
# Retain existing hashed bundle URLs for clients already using the previous catalog.
if [[ "$previous" != "$version" ]]; then
    cp -an -- "$previous/." "$version/"
fi
verify_directory "$version"
[[ "$(cat "$version/catalog_0.1.hash")" == "$new_hash" ]]
find "$version" -type d -exec chmod 755 {} +
find "$version" -type f -exec chmod 644 {} +

if [[ "$previous" == "$active" ]]; then
    previous="$versions/previous-$stamp"
    [[ ! -e "$previous" ]]
    mv -- "$active" "$previous"
    changed=1
fi
changed=1
switch_link "$version"
curl --noproxy '*' --fail --silent --show-error "http://127.0.0.1:18761/StandaloneWindows64/catalog_0.1.hash?after=$stamp" | cmp - "$version/catalog_0.1.hash"
curl --noproxy '*' --fail --silent --show-error "http://127.0.0.1:18761/StandaloneWindows64/catalog_0.1.json?after=$stamp" | sha256sum | cut -d' ' -f1 > "$staging/public-catalog.sha256"
[[ "$(cat "$staging/public-catalog.sha256")" == "$(sha256sum "$version/catalog_0.1.json" | cut -d' ' -f1)" ]]
if [[ "$previous" != "$version" ]]; then
    rollback="$versions/rollback-$release_id-$stamp.sh"
    temporary="$site/.StandaloneWindows64.rollback-$stamp"
    {
        printf '#!/usr/bin/env bash\nset -euo pipefail\n'
        printf 'exec 9>/srv/hundun-release-publish.lock\nflock -n 9\n'
        printf 'ln -s -- %q %q\n' "$previous" "$temporary"
        printf 'mv -Tf -- %q %q\n' "$temporary" "$active"
    } > "$rollback"
    chmod 700 "$rollback"
    printf 'ROLLBACK_COMMAND=bash %s\n' "$rollback"
fi
committed=1
printf 'SERVER_MATCHING_RESOURCES_PUBLISHED=%s\n' "$version"
