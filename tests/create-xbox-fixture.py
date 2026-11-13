"""Copy only the mod's verified byte ranges into private sparse test files."""
import ctypes
import hashlib
import json
import msvcrt
from pathlib import Path
import sys

source, profile_path, target = map(Path, sys.argv[1:])
assert not target.exists()
profile = json.loads(profile_path.read_text(encoding="utf-8"))
records = {}
for entry in profile["entries"]:
    for ref, expected in ((entry, entry["original_sha256"]),
                          (entry["operation"].get("source"), None)):
        if ref is None:
            continue
        expected = expected or ref["sha256"]
        with (source / ref["path"]).open("rb") as stream:
            stream.seek(ref["offset"])
            data = stream.read(ref["length"])
        assert len(data) == ref["length"]
        assert hashlib.sha256(data).hexdigest() == expected
        records[(ref["path"], ref["offset"], ref["length"])] = data
kernel = ctypes.WinDLL("kernel32", use_last_error=True)
kernel.DeviceIoControl.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_void_p,
    ctypes.c_ulong, ctypes.c_void_p, ctypes.c_ulong, ctypes.c_void_p, ctypes.c_void_p]
for relative in sorted({key[0] for key in records}):
    output = target / relative
    output.parent.mkdir(parents=True, exist_ok=True)
    spans = [(offset, data) for (path, offset, length), data in records.items()
             if path == relative]
    with output.open("w+b") as stream:
        returned = ctypes.c_ulong()
        assert kernel.DeviceIoControl(msvcrt.get_osfhandle(stream.fileno()), 0x900C4,
            None, 0, None, 0, ctypes.byref(returned), None)
        stream.truncate(max(offset + len(data) for offset, data in spans) + 64)
        for offset, data in spans:
            stream.seek(offset)
            stream.write(data)
(target / "sce_sys").mkdir(exist_ok=True)
(target / "sce_sys/param.json").write_text(json.dumps({
    "titleId": profile["title_id"], "contentVersion": profile["content_version"]
}), encoding="utf-8")
print(f"Created private sparse fixture with {len(records)} verified ranges.")
