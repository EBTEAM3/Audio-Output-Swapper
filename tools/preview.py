"""
Builds a standalone preview of the settings panel so the design can be checked
in a plain browser.

Does exactly what the C# host does at runtime -- substitutes glass.css into the
/*{GLASS_CSS}*/ placeholder -- then appends a mock payload so the page has
something to render without a live audio stack behind it.

Only the panel is previewable. The toast is drawn natively in WPF
(src/AudioSwapper/Ui/ToastWindow.xaml) rather than in a browser, so that it does
not need a Chromium stack running behind the tray icon all day.
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WEB = os.path.join(ROOT, "src", "AudioSwapper", "Web")


def read(name):
    with open(os.path.join(WEB, name), encoding="utf-8") as handle:
        return handle.read()


def icon_set():
    """Parses the icon table straight out of DeviceIcons.cs.

    Reading the real source keeps the preview honest: if a path is malformed
    there, the preview shows the same broken glyph the app would.
    """
    source_path = os.path.join(ROOT, "src", "AudioSwapper", "Ui", "DeviceIcons.cs")
    with open(source_path, encoding="utf-8") as handle:
        source = handle.read()

    icons = []
    pattern = re.compile(
        r'new DeviceIcon\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*new\[\]\s*\{(.*?)\}\s*\)',
        re.DOTALL,
    )
    for key, label, body in pattern.findall(source):
        paths = re.findall(r'"((?:[^"\\]|\\.)*)"', body)
        icons.append({"key": key, "label": label, "paths": paths})
    return icons


MOCK_DEVICES = [
    {
        "id": "dev-speakers",
        "name": "Speakers (Realtek(R) Audio)",
        "adapter": "Realtek High Definition Audio",
        "iconKey": "speakers",
        "active": True,
        "isDefault": True,
        "slot": "A",
        "stateLabel": "",
    },
    {
        "id": "dev-headphones",
        "name": "Headphones (WH-1000XM5)",
        "adapter": "Sony Audio Device",
        "iconKey": "headphones",
        "active": True,
        "isDefault": False,
        "slot": "B",
        "stateLabel": "",
    },
    {
        "id": "dev-projector",
        "name": "EPSON PJ (NVIDIA High Definition Audio)",
        "adapter": "NVIDIA High Definition Audio",
        "iconKey": "projector",
        "active": True,
        "isDefault": False,
        "slot": None,
        "stateLabel": "",
    },
    {
        "id": "dev-buds",
        "name": "Galaxy Buds3 Pro",
        "adapter": "Bluetooth Audio",
        "iconKey": "earbuds",
        "active": False,
        "isDefault": False,
        "slot": None,
        "stateLabel": "Not connected",
    },
]


def build(theme):
    css = read("glass.css")

    menu = read("menu.html").replace("/*{GLASS_CSS}*/", css)
    menu += (
        "\n<script>apply(%s);</script>\n"
        % json.dumps(
            {
                "type": "state",
                "theme": theme,
                "shell": "alpha",
                "icons": icon_set(),
                "devices": MOCK_DEVICES,
                "options": {
                    "alsoSetCommunications": True,
                    "showToast": True,
                    "trayReflectsDevice": True,
                    "startWithWindows": False,
                    "hotkeyEnabled": True,
                    "hotkey": "Ctrl+Alt+A",
                    "theme": theme,
                },
                "hotkeyError": None,
            }
        )
    )

    return menu


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "build", "preview")
    os.makedirs(out_dir, exist_ok=True)

    for theme in ("dark", "light"):
        path = os.path.join(out_dir, "menu-%s.html" % theme)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(build(theme))
        print(path)


if __name__ == "__main__":
    main()
