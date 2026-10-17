"""Synthetic, in-memory checks for the offline binary metadata reader."""

import struct
import unittest
from inspect_native import Minidump, pe_summary


def dump_fixture():
    data = bytearray(2048)
    data[:4] = b"MDMP"
    struct.pack_into("<II", data, 8, 4, 32)
    for index, stream in enumerate(((7, 56, 128), (4, 112, 192), (5, 20, 512), (6, 168, 640))):
        struct.pack_into("<III", data, 32 + index * 12, *stream)
    struct.pack_into("<H", data, 128, 9)
    struct.pack_into("<I", data, 192, 1)
    struct.pack_into("<QIIII", data, 196, 0x180000000, 0x10000, 0, 0, 400)
    name = "C:\\synthetic-private-path\\example.dll".encode("utf-16-le")
    struct.pack_into("<I", data, 400, len(name))
    data[404:404 + len(name)] = name
    struct.pack_into("<III", data, 228, 0x00010002, 0x00030004, 0)
    struct.pack_into("<I", data, 512, 1)
    struct.pack_into("<QII", data, 516, 0x1000, 128, 1024)
    struct.pack_into("<I", data, 640, 0x1234)
    struct.pack_into("<IIQQI", data, 648, 0xC000027B, 1, 0, 0x180000010, 2)
    struct.pack_into("<QQ", data, 680, 0x1000, 1)
    struct.pack_into("<Q", data, 1024, 0x1010)
    struct.pack_into("<IIIIQIIQ", data, 1040,
                     56, 0x53453032, 0x8000FFFF, 0x1235, 0x180000020, 8, 2, 0x1060)
    struct.pack_into("<QQ", data, 1120, 0x180000100, 0x180000200)
    return data


def pe_fixture():
    data = bytearray(1024)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 128)
    data[128:132] = b"PE\0\0"
    struct.pack_into("<HH", data, 132, 0x8664, 1)
    struct.pack_into("<H", data, 148, 240)
    struct.pack_into("<H", data, 152, 0x20B)
    struct.pack_into("<II", data, 272, 0x1000, 40)
    struct.pack_into("<IIII", data, 400, 512, 0x1000, 512, 512)
    struct.pack_into("<IIIII", data, 512, 0, 0, 0, 0x1080, 0)
    data[640:653] = b"KERNEL32.dll\0"
    return data


class MetadataTests(unittest.TestCase):
    def test_stowed_result_and_recorded_stack_come_from_memory_descriptors(self):
        summary = Minidump(dump_fixture()).summary()
        self.assertEqual(summary["exception"]["code"], "0xC000027B")
        self.assertEqual(summary["exception"]["address"], "example.dll+0x10")
        self.assertEqual(summary["stowed"][0]["hresult"], "0x8000FFFF")
        self.assertEqual(summary["stowed"][0]["thread_id"], 0x1234)
        self.assertEqual(summary["stowed"][0]["recorded_stack"],
                         ["example.dll+0x100", "example.dll+0x200"])
        self.assertNotIn("synthetic-private-path", str(summary))

    def test_memory_reads_can_span_adjacent_descriptors(self):
        dump = Minidump(dump_fixture())
        dump.memory = [(0x1000, 16, 1024), (0x1010, 112, 1040)]
        self.assertEqual(dump.read_memory(0x1008, 16), bytes(dump.data[1032:1048]))

    def test_missing_memory_is_reported_instead_of_inventing_a_stack(self):
        dump = Minidump(dump_fixture())
        dump.memory = [(0x1000, 80, 1024)]
        result = dump.summary()["stowed"][0]
        self.assertNotIn("recorded_stack", result)
        self.assertIn("not captured", result["stack_limitation"])

    def test_unknown_stowed_signature_is_rejected(self):
        data = dump_fixture()
        struct.pack_into("<I", data, 1044, 0)
        result = Minidump(data).summary()
        self.assertNotIn("stowed", result)
        self.assertIn("Unsupported", result["stowed_limitation"])

    def test_other_outer_exceptions_are_not_treated_as_stowed(self):
        data = dump_fixture()
        struct.pack_into("<I", data, 648, 0xC0000005)
        self.assertNotIn("stowed", Minidump(data).summary())

    def test_truncated_dump_is_rejected(self):
        with self.assertRaises(ValueError):
            Minidump(b"MDMP")

    def test_pe_import_name_is_read_without_loading_the_dll(self):
        result = pe_summary(pe_fixture())
        self.assertEqual(result["machine"], "0x8664")
        self.assertEqual(result["imports"], ["KERNEL32.dll"])
        self.assertEqual(result["delayed_imports"], [])

    def test_invalid_import_rva_is_rejected(self):
        data = pe_fixture()
        struct.pack_into("<I", data, 524, 0x9000)
        with self.assertRaises(ValueError):
            pe_summary(data)


if __name__ == "__main__":
    unittest.main()
