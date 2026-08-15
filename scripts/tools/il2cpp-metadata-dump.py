#!/usr/bin/env python3
"""Extract a type/member inventory from a Unity IL2CPP global-metadata.dat (v29).

Minimal dependency-free parser based on Il2CppDumper's metadata struct layouts.
Feed it the real installed game metadata and diff the output against
docs/gwyf-hook-inventory.md after every game update.

Usage:
    python3 scripts/tools/il2cpp-metadata-dump.py <global-metadata.dat> > types.json

No game launch required; reads the file only.
"""
import json
import struct
import sys

MAGIC = 0xFAB11BAF


def read_str(buf, base, index):
    if index < 0:
        return None
    off = base + index
    end = buf.find(b"\x00", off)
    if end == -1:
        return None
    return buf[off:end].decode("utf-8", errors="replace")


def main(path):
    buf = open(path, "rb").read()
    sanity, version = struct.unpack_from("<II", buf, 0)
    if sanity != MAGIC:
        sys.exit("not an IL2CPP metadata file")

    off = 8
    pairs = []
    for _ in range(41):
        o, s = struct.unpack_from("<Ii", buf, off)
        pairs.append((o, s))
        off += 8

    string_off = pairs[2][0]
    methods_off, methods_size = pairs[5]
    fields_off, fields_size = pairs[11]
    props_off, props_size = pairs[4]
    typedefs_off, typedefs_size = pairs[19]
    images_off, images_size = pairs[20]
    assemblies_off, assemblies_size = pairs[21]

    prop_count = props_size // 20
    properties = []
    for i in range(prop_count):
        o = props_off + i * 20
        name_index, get_idx, set_idx, attrs, token = struct.unpack_from("<IiiII", buf, o)
        properties.append(
            {
                "name": read_str(buf, string_off, name_index),
                "get": get_idx,
                "set": set_idx,
            }
        )

    method_count = methods_size // 32
    methods = []
    for i in range(method_count):
        o = methods_off + i * 32
        name_index, declaring_type, return_type, param_start, gen_container, token = struct.unpack_from(
            "<IiiiiI", buf, o
        )
        flags, iflags, slot, param_count = struct.unpack_from("<HHHH", buf, o + 24)
        methods.append(
            {
                "name": read_str(buf, string_off, name_index),
                "declaringType": declaring_type,
                "paramStart": param_start,
                "paramCount": param_count,
                "flags": flags,
            }
        )

    field_count = fields_size // 12
    fields = []
    for i in range(field_count):
        o = fields_off + i * 12
        name_index, type_index, token = struct.unpack_from("<IiI", buf, o)
        fields.append(
            {
                "name": read_str(buf, string_off, name_index),
                "typeIndex": type_index,
            }
        )

    typedef_count = typedefs_size // 88
    typedefs = []
    for i in range(typedef_count):
        o = typedefs_off + i * 88
        values = struct.unpack_from("<IIiiiiiIiiiiiiiiHHHHHHHHII", buf, o)
        name_index, namespace_index = values[0], values[1]
        field_start, method_start = values[8], values[9]
        property_start = values[11]
        method_count_v, property_count_v, field_count_v = values[16], values[17], values[18]
        typedefs.append(
            {
                "name": read_str(buf, string_off, name_index),
                "namespace": read_str(buf, string_off, namespace_index),
                "fieldStart": field_start,
                "fieldCount": field_count_v,
                "methodStart": method_start,
                "methodCount": method_count_v,
                "propertyStart": property_start,
                "propertyCount": property_count_v,
            }
        )

    image_count = images_size // 40
    assembly_count = assemblies_size // 64
    assembly_names = []
    for i in range(assembly_count):
        o = assemblies_off + i * 64
        image_index, token, ref_start, ref_count = struct.unpack_from("<iiii", buf, o)
        name_index = struct.unpack_from("<I", buf, o + 16)[0]
        assembly_names.append(read_str(buf, string_off, name_index))

    result = {"version": version, "images": []}
    for i in range(image_count):
        o = images_off + i * 40
        name_index, assembly_index, type_start, type_count = struct.unpack_from("<IIiI", buf, o)
        types = []
        for t in typedefs[type_start : type_start + type_count]:
            entry = {"name": t["name"], "namespace": t["namespace"]}
            entry["methods"] = [
                methods[m]["name"]
                for m in range(t["methodStart"], t["methodStart"] + t["methodCount"])
                if m < len(methods)
            ]
            entry["fields"] = [
                fields[f]["name"]
                for f in range(t["fieldStart"], t["fieldStart"] + t["fieldCount"])
                if f < len(fields)
            ]
            entry["properties"] = [
                properties[p]["name"]
                for p in range(t["propertyStart"], t["propertyStart"] + t["propertyCount"])
                if p < len(properties)
            ]
            types.append(entry)
        result["images"].append(
            {
                "name": read_str(buf, string_off, name_index),
                "assemblyName": (
                    assembly_names[assembly_index] if 0 <= assembly_index < len(assembly_names) else None
                ),
                "types": types,
            }
        )

    json.dump(result, sys.stdout)
    sys.stderr.write(
        f"version={version} images={len(result['images'])} "
        f"types={len(typedefs)} methods={len(methods)} fields={len(fields)}\n"
    )


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])