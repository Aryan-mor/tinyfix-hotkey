# Third-party notices

TinyFix itself is licensed as described in `LICENSE`. The C# application in `src/` and `tests/` adapts ideas and a
small amount of code from the open-source project below, and uses the NuGet packages listed after it.

## Filee (adapted code and layout)

Project layout, central package management, the editor and format rules, the JSON localization mechanism
(`LocalizationService`, the language registry and its OS-language resolution rules), the single-instance mechanism,
the transparent-window and tray patterns, the Settings navigation chrome, and the donut (radial menu) were adapted
from Filee by KnifeLemon (https://github.com/KnifeLemon/Filee). The adapted file groups are:

* Donut drawing: `src/TinyFix.App/Controls/DonutMenu.cs`, `DonutMenu.Flip.cs`, `DonutMenu.Groups.cs`,
  `DonutGeometry.cs`, `SliceLabelLayout.cs`, `SliceIcons.cs`, `SliceIcons.Data.cs`
* Donut window and controller: `src/TinyFix.App/Views/RadialWindow.axaml`, `RadialWindow.axaml.cs`,
  `src/TinyFix.App/Services/RadialController.cs`, `ActivationGrace.cs`, `TransparentWindows.cs`
* Look and motion: `src/TinyFix.App/Styles/Soft.axaml`, `Controls.axaml`, `Icons.axaml`,
  `src/TinyFix.App/Services/SoftPalette.cs`, `Motion.cs`, `UiZoom.cs`, `WindowsRendering.cs`
* App shell patterns: `LocalizationService.cs`, `AppLanguages.cs`, `SingleInstance.cs`, `TrayService.cs`
* Tests: `tests/TinyFix.App.Tests/LocalizationTests.cs`, `DonutGeometryTests.cs`, `DonutAnimationTests.cs`,
  `DonutHighlightTests.cs`, `SliceLabelLayoutTests.cs`, `SliceIconsTests.cs`, `RenderTests.cs`

Filee's file-conversion features, engines, installer, preset gallery and update machinery are not part of TinyFix.
Icon geometries in `Styles/Icons.axaml` and `Controls/SliceIcons.Data.cs` are from Material Design Icons by
Pictogrammers (Apache-2.0).

```text
MIT License

Copyright (c) 2026 Myeonggu Kim (KnifeLemon) and Filee contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Prompt style rules

The default Fix and Translate prompts carry style rules adapted from the No-Slop skill by Soheil Momeni (MIT License,
https://github.com/soheilmomeniii/no-slop) and the AI De-Slopifier by Steven Boylan
(https://github.com/StevenBoylan/de-slopifier), as noted inside the prompts themselves.

## NuGet packages

| Package | License |
| --- | --- |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Headless, Avalonia.Headless.XUnit | MIT |
| CommunityToolkit.Mvvm | MIT |
| SharpHook | MIT |
| Microsoft.Extensions.Logging.Abstractions | MIT |
| xunit.v3, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |

Avalonia brings its own dependencies (SkiaSharp, HarfBuzzSharp and others); their notices ship with the packages.
