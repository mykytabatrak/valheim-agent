from pathlib import Path
import os
import signal
import subprocess
import time

root = Path(__file__).resolve().parent
workspace = root.parents[1]
game = Path(os.environ.get('VALHEIM_DIR', str(Path.home()/'Library/Application Support/Steam/steamapps/common/Valheim')))/'valheim.app/Contents/MacOS/Valheim'
pids = subprocess.run(['pgrep','-f',str(game)],capture_output=True,text=True).stdout.split()
if pids: raise SystemExit('Close the running Valheim session before starting the isolated test.')
control = root/'control'
saves = root/'saves'
control.mkdir(parents=True,exist_ok=True)
for name in ('command.json','state.json'):
    (control/name).unlink(missing_ok=True)
saves.mkdir(parents=True,exist_ok=True)
env = os.environ.copy()
env.update(VALHEIM_BRIDGE_MODE='test',VALHEIM_BRIDGE_FIXTURE_SAFE='1',VALHEIM_BRIDGE_DIR=str(control),VALHEIM_BRIDGE_SAVES=str(saves),DOORSTOP_ENABLED='1',DOORSTOP_IGNORE_DISABLED_ENV='1',DOORSTOP_TARGET_ASSEMBLY=str(root/'runtime/ValheimBridge.dll'),DYLD_INSERT_LIBRARIES=str(root/'runtime/libdigfloor.dylib'),SteamAppId='892970',SteamGameId='892970')
log = open(control/'stdout.log','w')
p = subprocess.Popen([str(game),'-savedir',str(saves),'-screen-fullscreen','0','-screen-width','1280','-screen-height','800','-logFile',str(control/'Player.log')],env=env,stdout=log,stderr=log,start_new_session=True)
print('Started isolated test game',p.pid)
