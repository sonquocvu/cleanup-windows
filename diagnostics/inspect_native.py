"""Offline, read-only PE/minidump metadata reader; Python standard library only.

This is not a debugger or stack unwinder. It emits module-relative addresses,
never arbitrary dump strings, error text, environment blocks, or module paths.
"""

import argparse
import hashlib
import json
import ntpath
from pathlib import Path
import struct


def unpack(data, offset, fmt):
    size = struct.calcsize(fmt)
    if offset < 0 or offset + size > len(data):
        raise ValueError("Truncated binary metadata")
    return struct.unpack_from(fmt, data, offset)


def bounded_count(count, maximum=4096):
    if count > maximum:
        raise ValueError("Metadata count exceeds diagnostic limit")
    return count


class Minidump:
    def __init__(self, data):
        self.data = data
        if data[:4] != b"MDMP":
            raise ValueError("Not a minidump")
        count, directory = unpack(data, 8, "<II")
        self.streams = {}
        for index in range(bounded_count(count)):
            kind, size, rva = unpack(data, directory + 12 * index, "<III")
            if rva + size > len(data):
                raise ValueError("Stream extends past file")
            self.streams[kind] = (rva, size)
        if 7 not in self.streams or unpack(data, self.streams[7][0], "<H")[0] != 9:
            raise ValueError("Only AMD64 minidumps are supported")
        self.memory = []
        if 5 in self.streams:
            start = self.streams[5][0]
            for index in range(bounded_count(unpack(data, start, "<I")[0], 100000)):
                address, size, rva = unpack(data, start + 4 + 16 * index, "<QII")
                self.memory.append((address, size, rva))
        if 9 in self.streams:
            start = self.streams[9][0]
            count, rva = unpack(data, start, "<QQ")
            for index in range(bounded_count(count, 100000)):
                address, size = unpack(data, start + 16 + 16 * index, "<QQ")
                self.memory.append((address, size, rva))
                rva += size
        if 3 in self.streams:
            start = self.streams[3][0]
            for index in range(bounded_count(unpack(data, start, "<I")[0])):
                address, size, rva = unpack(data, start + 4 + 48 * index + 24, "<QII")
                self.memory.append((address, size, rva))
        self.modules = []
        if 4 in self.streams:
            start = self.streams[4][0]
            for index in range(bounded_count(unpack(data, start, "<I")[0])):
                offset = start + 4 + index * 108
                base, size, _, _, name_rva = unpack(data, offset, "<QIIII")
                length = unpack(data, name_rva, "<I")[0]
                bounded_count(length, 32768)
                name_bytes = data[name_rva + 4:name_rva + 4 + length]
                if len(name_bytes) != length:
                    raise ValueError("Truncated module name")
                name = ntpath.basename(name_bytes.decode("utf-16-le"))
                high, low = unpack(data, offset + 32, "<II")
                version = f"{high >> 16}.{high & 65535}.{low >> 16}.{low & 65535}"
                self.modules.append(dict(name=name, base=base, size=size, version=version))

    def read_memory(self, address, size):
        bounded_count(size, 1024 * 1024)
        cursor, result = address, bytearray()
        while cursor < address + size:
            available = [(base, length, rva) for base, length, rva in self.memory
                         if base <= cursor < base + length]
            if not available:
                raise ValueError("Required virtual memory was not captured in this dump")
            base, length, rva = max(available, key=lambda part: part[0] + part[1])
            count = min(base + length - cursor, address + size - cursor)
            offset = rva + cursor - base
            if offset < 0 or offset + count > len(self.data):
                raise ValueError("Captured memory extends past file")
            result.extend(self.data[offset:offset + count])
            cursor += count
        return bytes(result)

    def relative(self, address):
        for module in self.modules:
            if module["base"] <= address < module["base"] + module["size"]:
                return f"{module['name']}+0x{address - module['base']:X}"
        return "<address outside captured module ranges>"

    def stowed(self, address):
        header = self.read_memory(address, 8)
        size, signature = unpack(header, 0, "<II")
        if (signature, size) not in ((0x53453031, 40), (0x53453032, 56)):
            raise ValueError("Unsupported stowed-exception signature or structure size")
        record = self.read_memory(address, size)
        hresult, packed = unpack(record, 8, "<II")
        result = dict(hresult=f"0x{hresult:08X}", form=packed & 3,
                      thread_id=packed & ~3, structure_bytes=size)
        if result["form"] == 1:
            exception_address, word_size, word_count, trace = unpack(record, 16, "<QIIQ")
            result["exception_address"] = self.relative(exception_address)
            if word_size != 8:
                raise ValueError("Unexpected stowed stack word size")
            bounded_count(word_count, 256)
            try:
                raw = self.read_memory(trace, word_count * word_size)
                addresses = struct.unpack("<" + "Q" * word_count, raw)
                result["recorded_stack"] = [self.relative(value) for value in addresses]
            except ValueError as error:
                result["stack_limitation"] = str(error)
        elif result["form"] == 2:
            result["text_omitted"] = True
        else:
            raise ValueError("Unknown stowed exception form")
        if size == 56:
            nested_type = unpack(record, 40, "<I")[0]
            result["nested_type"] = f"0x{nested_type:08X}"
            result["nested_object_not_decoded"] = nested_type != 0
        return result

    def summary(self):
        output = dict(stream_types=sorted(self.streams),
                      module_count=len(self.modules), memory_ranges=len(self.memory))
        output["relevant_modules"] = [dict(name=m["name"], version=m["version"])
            for m in self.modules if m["name"].lower().startswith(
                ("microsoft.ui", "microsoft.windows", "coremessaging", "combase", "vcruntime", "msvcp"))]
        if 6 not in self.streams:
            output["limitation"] = "No exception stream"
            return output
        start = self.streams[6][0]
        thread = unpack(self.data, start, "<I")[0]
        code, flags, nested_record, address, count = unpack(self.data, start + 8, "<IIQQI")
        bounded_count(count, 15)
        parameters = unpack(self.data, start + 40, "<" + "Q" * count)
        output["exception"] = dict(code=f"0x{code:08X}", flags=flags, thread_id=thread,
                                   address=self.relative(address), parameter_count=count,
                                   chained_record_present=nested_record != 0)
        if code == 0xC000027B and count == 2:
            try:
                bounded_count(parameters[1], 16)
                pointers = self.read_memory(parameters[0], parameters[1] * 8)
                output["stowed"] = [self.stowed(pointer) for pointer in
                    struct.unpack("<" + "Q" * parameters[1], pointers)]
            except ValueError as error:
                output["stowed_limitation"] = str(error)
        else:
            output["stowed_limitation"] = "Exception stream is not a two-parameter STATUS_STOWED_EXCEPTION"
        output["symbol_limit"] = "No symbol lookup or native stack unwinding was performed"
        return output


def pe_summary(data):
    if data[:2] != b"MZ":
        raise ValueError("Not a PE image")
    nt = unpack(data, 0x3C, "<I")[0]
    if data[nt:nt + 4] != b"PE\0\0":
        raise ValueError("Invalid PE header")
    machine, count = unpack(data, nt + 4, "<HH")
    optional_size = unpack(data, nt + 20, "<H")[0]
    optional = nt + 24
    if unpack(data, optional, "<H")[0] != 0x20B:
        raise ValueError("Only PE32+ images are supported")
    sections = [unpack(data, optional + optional_size + 40 * index + 8, "<IIII")
                for index in range(bounded_count(count, 96))]

    def rva_to_offset(rva):
        for virtual_size, virtual_address, raw_size, raw_offset in sections:
            if virtual_address <= rva < virtual_address + min(virtual_size, raw_size):
                return raw_offset + rva - virtual_address
        raise ValueError("RVA is not backed by captured PE file bytes")

    def import_name(rva):
        offset = rva_to_offset(rva)
        end = data.find(b"\0", offset, offset + 512)
        if end < 0:
            raise ValueError("Unterminated import name")
        return data[offset:end].decode("ascii")

    imports = []
    import_rva, import_size = unpack(data, optional + 112 + 8, "<II")
    if import_rva:
        table = rva_to_offset(import_rva)
        for index in range(bounded_count(import_size // 20)):
            descriptor = unpack(data, table + index * 20, "<IIIII")
            if not any(descriptor):
                break
            imports.append(import_name(descriptor[3]))
    delayed = []
    delay_rva, delay_size = unpack(data, optional + 112 + 13 * 8, "<II")
    if delay_rva:
        table = rva_to_offset(delay_rva)
        for index in range(bounded_count(delay_size // 32)):
            descriptor = unpack(data, table + index * 32, "<IIIIIIII")
            if not any(descriptor):
                break
            if descriptor[0] != 1:
                raise ValueError("Only RVA-based delay imports are supported")
            delayed.append(import_name(descriptor[1]))
    return dict(machine=f"0x{machine:04X}", imports=imports, delayed_imports=delayed)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=("dump", "pe"))
    parser.add_argument("path", type=Path)
    args = parser.parse_args()
    data = args.path.read_bytes()
    output = Minidump(data).summary() if args.kind == "dump" else pe_summary(data)
    output["file"] = args.path.name
    output["sha256"] = hashlib.sha256(data).hexdigest().upper()
    print(json.dumps(output, indent=2))


if __name__ == "__main__":
    main()
