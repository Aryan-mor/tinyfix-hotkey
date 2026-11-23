#!/bin/sh
# Removes what install.sh wrote. Settings in ~/.config/tinyfix are left in place.
set -eu
rm -f "${HOME}/.local/bin/tinyfix"
rm -f "${HOME}/.local/share/applications/tinyfix.desktop"
rm -rf "${HOME}/.local/share/tinyfix"
echo "TinyFix was removed for this user."
