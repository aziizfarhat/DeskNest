# Vendored installer translations

## ChineseSimplified.isl

Inno Setup ships Arabic, French, German, Italian, Japanese, Korean, Portuguese, Russian, Spanish and Ukrainian messages, but not Simplified Chinese. This file is vendored so the DeskNest installer can be built in all twelve languages without requiring a separate download.

- Source: <https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation>
- File: `ChineseSimplified.isl`
- Maintainer: Zhenghan Yang (Kira)
- Requires Inno Setup 6.5.0 or later (the version used for DeskNest release builds).

The file is used as-is by `installer.iss`:

```ini
Name: "chinesesimplified"; MessagesFile: "installer-lang\ChineseSimplified.isl"
```
