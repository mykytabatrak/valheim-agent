"""Run individual checks in an already loaded, isolated fixture world."""
import argparse
import sys
import time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from control import send,state

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('check',choices=['heartbeat','damage','raid'])
    args=parser.parse_args()
    s=state()
    if s.get('mode')!='test' or s.get('world')!='Codex Local Test' or not s.get('ready'):
        raise SystemExit('Checks require the loaded isolated fixture world.')
    send('arm')
    if args.check=='heartbeat':
        send('stop');time.sleep(9)
        assert not state()['controlled'],'Lease did not release controls'
        try:send('move',x=1,z=0,seconds=1)
        except RuntimeError:pass
        else:raise AssertionError('Movement accepted without a lease')
    else:
        before=s['health']
        try:send('test_damage' if args.check=='damage' else 'test_raid')
        except RuntimeError as error:
            if 'HANDOFF' not in str(error):raise
        for _ in range(30):
            time.sleep(.2);s=state()
            if s.get('handoff'):break
        assert s.get('handoff') and not s['controlled'],s['lastResult']
        if args.check=='damage':assert s['health']<before
        else:assert s['handoff'].startswith('RAID:')
        # Remain handed off. Raid fixtures have no spawns; close the test game afterward.
    print('PASS:',args.check)
if __name__=='__main__':main()
