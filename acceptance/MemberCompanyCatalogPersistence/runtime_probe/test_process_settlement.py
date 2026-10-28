import signal,unittest
from unittest.mock import patch
from process_settlement import settle

class SettlementControls(unittest.TestCase):
    def test_already_terminal_never_signals_and_never_claims_descendants(self):
        result=settle(42,lambda fd:True,lambda *_:self.fail('signal forbidden'),lambda *_:self.fail('wait forbidden'))
        self.assertTrue(result['terminalVerified']);self.assertEqual('unproved',result['descendantSettlement']);self.assertFalse(result['termSent'])
    def test_exact_handle_term_then_kill_when_needed(self):
        events=[];waits=iter([False,True])
        with patch.object(signal,'SIGKILL',9,create=True):
            result=settle(42,lambda _:False,lambda fd,sig:events.append((fd,sig)),lambda fd,seconds:(events.append((fd,seconds)) or next(waits)))
        self.assertEqual([(42,signal.SIGTERM),(42,2),(42,9),(42,5)],events);self.assertTrue(result['terminalVerified']);self.assertTrue(result['killSent']);self.assertEqual('unproved',result['descendantSettlement'])
    def test_missing_process_race_still_needs_terminal_evidence(self):
        def missing(*_):raise ProcessLookupError()
        with patch.object(signal,'SIGKILL',9,create=True):
            self.assertTrue(settle(42,lambda _:False,missing,lambda *_:True)['terminalVerified'])
            receipt={}
            with self.assertRaises(RuntimeError):settle(42,lambda _:False,missing,lambda *_:False,receipt)
            self.assertFalse(receipt['terminalVerified']);self.assertEqual('RuntimeError',receipt['cleanupFailure']);self.assertEqual('unproved',receipt['descendantSettlement'])
        with self.assertRaises(ValueError):settle(-1,lambda _:True,lambda *_:None,lambda *_:True)

if __name__=='__main__':unittest.main()
