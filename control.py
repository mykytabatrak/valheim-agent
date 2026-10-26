"""Send a bounded command to the isolated local test game."""
from pathlib import Path
import json
import os
import sys
import time
import uuid

root = Path(os.environ.get('VALHEIM_BRIDGE_DIR', str(Path(__file__).resolve().parent/'control')))
def state():
    for _ in range(20):
        try: return json.loads((root/'state.json').read_text())
        except (FileNotFoundError,json.JSONDecodeError): time.sleep(.05)
    raise RuntimeError('No bridge state')
def send(action, **values):
    identity = uuid.uuid4().hex[:8]
    command = dict(id=identity,action=action,**values)
    path=root/'command.json'
    if path.exists(): raise RuntimeError('Previous command still pending')
    temp=root/'command.tmp'; temp.write_text(json.dumps(command)); os.replace(temp,path)
    for _ in range(100):
        s=state()
        if s.get('lastResult','').startswith(identity):
            if ': ERROR ' in s['lastResult']: raise RuntimeError(s['lastResult'])
            return s
        if s.get("handoff") and action not in ("arm","release","status"): raise RuntimeError("HANDOFF: "+s["handoff"])
        time.sleep(.1)
    raise TimeoutError('No command acknowledgement')
if __name__ == '__main__':
    action=sys.argv[1] if len(sys.argv)>1 else 'status'
    kwargs=json.loads(sys.argv[2]) if len(sys.argv)>2 else {}
    print(json.dumps(send(action,**kwargs),indent=2))
