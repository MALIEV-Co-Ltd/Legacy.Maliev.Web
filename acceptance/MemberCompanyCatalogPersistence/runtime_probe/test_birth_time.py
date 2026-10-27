import unittest
from birth_time import estimate

class BirthTimeControls(unittest.TestCase):
    def test_exact_ticks_retained_and_utc_precision_marked(self):
        result=estimate('cpu 1\nbtime 1000\n','150',100)
        self.assertEqual('150',result['startBootTicks']);self.assertEqual(100,result['clockTicksPerSecond']);self.assertIn('one second',result['precision']);self.assertEqual('1970-01-01T00:16:41.500000+00:00',result['startUtcApprox'])
        for raw,ticks,hz in (('btime 1\nbtime 2\n','1',100),('btime 1\n','0',100),('btime 1\n','1',0),('btime 0\n','1',100)):
            with self.subTest(raw=raw,ticks=ticks,hz=hz),self.assertRaises(ValueError):estimate(raw,ticks,hz)

if __name__=='__main__':unittest.main()
