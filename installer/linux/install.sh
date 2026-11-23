#!/bin/sh
# Per-user install: no root. Copies this folder's TinyFix binary and adds a launcher.
set -eu
here=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
root="${HOME}/.local/share/tinyfix"
bin_dir="${HOME}/.local/bin"
apps="${HOME}/.local/share/applications"
mkdir -p "$root" "$bin_dir" "$apps"
cp "$here/TinyFix" "$root/TinyFix"
chmod +x "$root/TinyFix"
ln -sfn "$root/TinyFix" "$bin_dir/tinyfix"
icon_line=""
if [ -f "$here/tinyfix.png" ]; then
  cp "$here/tinyfix.png" "$root/tinyfix.png"
  icon_line="Icon=$root/tinyfix.png"
fi
# The Icon line is omitted when the archive has no png (ImageMagick could not read the ico).
cat > "$apps/tinyfix.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=TinyFix
Comment=Fix typos with a shortcut
Exec=$root/TinyFix
Terminal=false
Categories=Utility;
$icon_line
EOF
echo "Installed for this user: $root/TinyFix"
echo "Command: $bin_dir/tinyfix (add ~/.local/bin to PATH if it is not already)."
