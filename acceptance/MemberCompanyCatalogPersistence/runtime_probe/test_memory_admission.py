import unittest
from memory_admission import available_kib,MIN_AVAILABLE_KIB

class MemoryControls(unittest.TestCase):
    def test_fresh_physical_available_only_with_unchanged_floor(self):
        self.assertEqual(MIN_AVAILABLE_KIB,available_kib(f'MemTotal: 9000000 kB\nMemAvailable: {MIN_AVAILABLE_KIB} kB\n'))
        for raw in (f'MemAvailable: {MIN_AVAILABLE_KIB-1} kB\n','MemFree: 9999999 kB\n','MemAvailable: 9999999 kB\nMemAvailable: 9999999 kB\n','MemAvailable: -1 kB\n'):
            with self.subTest(raw=raw),self.assertRaises(ValueError):available_kib(raw)

if __name__=='__main__':unittest.main()
