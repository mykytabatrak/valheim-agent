"""Open the user's normal Valheim saves with the bridge available but disarmed."""
from pathlib import Path
import os,subprocess
root=Path(__file__).resolve().parent
workspace=root.parents[1]
game=Path(os.environ.get('VALHEIM_DIR', str(Path.home()/'Library/Application Support/Steam/steamapps/common/Valheim')))/'valheim.app/Contents/MacOS/Valheim'
if subprocess.run(['pgrep','-f',str(game)],capture_output=True,text=True).stdout.strip():
    raise SystemExit('Close Valheim before using this launcher.')
control=root/'control';control.mkdir(parents=True,exist_ok=True)
for name in ('command.json','state.json'): (control/name).unlink(missing_ok=True)
env=os.environ.copy();env.update(VALHEIM_BRIDGE_MODE='server',VALHEIM_BRIDGE_DIR=str(control),VALHEIM_BRIDGE_SAVES=str(root/'saves'),DOORSTOP_ENABLED='1',DOORSTOP_IGNORE_DISABLED_ENV='1',DOORSTOP_TARGET_ASSEMBLY=str(root/'runtime/ValheimBridge.dll'),DYLD_INSERT_LIBRARIES=str(root/'runtime/libdigfloor.dylib'),SteamAppId='892970',SteamGameId='892970')
log=open(control/'stdout.log','w')
p=subprocess.Popen([str(game),'-screen-fullscreen','0','-logFile',str(control/'Player.log')],env=env,stdout=log,stderr=log,start_new_session=True)
print('Valheim opened with bridge disarmed. Join normally; Codex can arm only after checking the joined world.',p.pid)
