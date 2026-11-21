#!/usr/bin/env bash
set -euxo pipefail

# Use the configured repositories directly when they can satisfy GTK#'s dependencies
# (e.g. Ubuntu 22.04). On 24.04/26.04, configure stable-focal with
# ci_linux_install_mono_from_microsoft_deb_packages.sh before running this script.
if apt-get --simulate install libgtk2.0-cil-dev > /dev/null 2>&1; then
    DEBIAN_FRONTEND=noninteractive apt install --yes libgtk2.0-cil-dev
    exit 0
fi

source /etc/os-release

if [[ "$VERSION_ID" == "26.04" ]]; then
    # stable-focal's GTK# bindings depend on the old libgdk-pixbuf2.0-0 name.
    # Noble's transitional package contains no libraries; both of its native
    # dependencies are available in 26.04, so don't add the whole Noble repo.
    if [[ "$(dpkg --print-architecture)" != "amd64" ]]; then
        echo "The pinned Noble compatibility package is only configured for amd64" >&2
        exit 1
    fi

    tmp_dir=$(mktemp -d)
    trap 'rm -rf "$tmp_dir"' EXIT
    package_path="$tmp_dir/libgdk-pixbuf2.0-0_2.40.2-3build2_amd64.deb"
    curl -fsSL \
        "https://archive.ubuntu.com/ubuntu/pool/universe/g/gdk-pixbuf-xlib/libgdk-pixbuf2.0-0_2.40.2-3build2_amd64.deb" \
        -o "$package_path"
    # SHA256 from Noble's universe/binary-amd64/Packages index.
    echo "d67779699f7b5094507df83c2df0badeb5fa531e612c8923b666acbb9e80b2c8  $package_path" | sha256sum --check
    DEBIAN_FRONTEND=noninteractive apt install --yes "$package_path"
fi

DEBIAN_FRONTEND=noninteractive apt install --yes libgtk2.0-cil-dev
