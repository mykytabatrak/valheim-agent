"""Integration checks against the actual game; fixtures are restricted to test mode."""
import sys,time,json,math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from control import send,state

def wait_ready():
    for _ in range(150):
        s=state()
        if s['ready']: return s
        time.sleep(.5)
    raise RuntimeError('Player did not load')
def arm():
    time.sleep(.1)
    return send('arm')
def command(action,**kw):
    s=state()
    if s.get('handoff'): raise RuntimeError('HANDOFF: '+s['handoff'])
    if not s.get('controlled'): arm()
    return send(action,**kw)
def pause(seconds):
    end=time.time()+seconds
    while time.time()<end:
        time.sleep(min(1,end-time.time()))
        command('heartbeat')
def goto(pos,within=1.5):
    from agent import Agent
    nav=Agent.__new__(Agent);nav.sleeping=True;nav.opened_doors=set()
    if not state().get('controlled'):arm()
    return nav.goto(pos,within)
def snapshot(name):
    s=state();Path(__file__).with_name(name+'.json').write_text(json.dumps(s,indent=2));return s
if __name__=='__main__':
    action=sys.argv[1]
    if action=='start':
        send('start');wait_ready();time.sleep(11);arm();command('test_travel',x=65,z=10);wait_ready();pause(8);print('READY',state()['position'])
    elif action=='fixture':
        command('test_fixture');pause(2);snapshot('fixture-evidence');print('Fixture created')
    elif action=='state':
        s=snapshot('current-test-state');print(json.dumps({k:s[k] for k in ['position','inventory','objects','plants','handoff','night','crops']},indent=2))
    else: raise RuntimeError('Unknown phase')
