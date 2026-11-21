TinyFix — select English text anywhere in Windows, then press F3.
The selected text is corrected in place (spelling + real-word mix-ups like form/from).

Install folder: this directory (keep it on D:).

Use
- Select text in Chrome, Notepad, Word, or any other app
- Press F3
- If nothing is selected, nothing happens
- Non-English text (for example Persian) is left unchanged
- A tray icon named TinyFix appears while it is running

Stop
- Right-click the TinyFix tray icon and choose Exit
- Or run stop.ps1 in this folder

Start again
- Run start.ps1, or sign in to Windows (a Startup shortcut is installed)

Uninstall
- Run uninstall.ps1, then delete this folder

Notes
- F3 is also "Find Next" in many programs. While TinyFix is running, F3 is used for correction instead.
- The tool cannot send keys into windows that are running as Administrator if TinyFix itself is not.
- Only English is corrected. It still misses some errors; it would rather leave a word alone than change one you meant.
- Your previous clipboard is restored after the paste.
