"""Read PE import tables without loading or executing the binaries."""
import json
from pathlib import Path
import struct
import sys


def imports(path):
    data = path.read_bytes()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        return []
    count = struct.unpack_from("<H", data, pe + 6)[0]
    size = struct.unpack_from("<H", data, pe + 20)[0]
    opt = pe + 24
    directories = opt + (112 if struct.unpack_from("<H", data, opt)[0] == 0x20B else 96)
    sections = [struct.unpack_from("<IIII", data, opt + size + 40 * i + 8) for i in range(count)]

    def offset(rva):
        for vsize, va, rawsize, raw in sections:
            if va <= rva < va + max(vsize, rawsize):
                return raw + rva - va
        return rva

    def name(rva):
        start = offset(rva)
        return data[start:data.index(b"\0", start)].decode("ascii")

    rva, size = struct.unpack_from("<II", data, directories + 8)
    result = []
    if rva:
        pos = offset(rva)
        while any(data[pos:pos + 20]):
            result.append(name(struct.unpack_from("<I", data, pos + 12)[0]))
            pos += 20
    # Delay-load DLLs are prerequisites too, even if the initial window can open.
    rva, size = struct.unpack_from("<II", data, directories + 13 * 8)
    if rva:
        pos = offset(rva)
        while any(data[pos:pos + 32]):
            attributes, name_rva = struct.unpack_from("<II", data, pos)
            if not attributes & 1:
                image_base = struct.unpack_from("<Q" if data[opt:opt + 2] == b"\x0b\x02" else "<I", data, opt + (24 if data[opt:opt + 2] == b"\x0b\x02" else 28))[0]
                name_rva -= image_base
            result.append(name(name_rva))
            pos += 32
    return result


if __name__ == "__main__":
    root = Path(sys.argv[1])
    found = {}
    for path in [*root.glob("*.dll"), *root.glob("*.exe")]:
        dependencies = [n for n in imports(path) if n.lower().startswith(("msvcp", "vcruntime", "concrt"))]
        if dependencies:
            found[path.name] = [{"file": name, "bundled": (root / name).is_file()} for name in dependencies]
    print(json.dumps(found, indent=2))
