# TinyFix on Linux

Global shortcuts need an X11 session. Under XWayland they may work; a native Wayland session does not deliver them, and TinyFix cannot read the caret there.

GNOME hides tray icons unless the AppIndicator extension is installed (AppIndicator and KStatusNotifierItem Support).

Run `./install.sh` to copy TinyFix into `~/.local/share/tinyfix` and add `~/.local/bin/tinyfix`. `./uninstall.sh` removes that copy. Your settings stay in `~/.config/tinyfix`.
