# Plugin icon generator

`make_icon.py` renders `icon.png` (repo root) from `lizard_flat.svg`.

```bash
pip install pillow numpy resvg-py
python tools/icon/make_icon.py
```

It also writes `preview.png` (light and dark background) next to the script; that file is not committed.

`lizard_flat.svg` is the lizard from [Fluent Emoji](https://github.com/microsoft/fluentui-emoji),
MIT License, Copyright (c) Microsoft Corporation.
