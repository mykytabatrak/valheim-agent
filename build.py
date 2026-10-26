#!/usr/bin/env python3
"""Compile against the user's installed game; no game libraries are redistributed."""
from pathlib import Path
import os
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parent
game = Path(os.environ.get('VALHEIM_DIR', str(Path.home() / 'Library/Application Support/Steam/steamapps/common/Valheim')))
managed = game / 'valheim.app/Contents/Resources/Data/Managed'
system_dotnet = shutil.which('dotnet')
sdk = Path(os.environ.get('DOTNET_ROOT', str(Path(system_dotnet).resolve().parent) if system_dotnet else str(root / '.tooling/dotnet')))
if '--native' in sys.argv:
    source = root / 'third_party/UnityDoorstop'
    files = ['src/bootstrap.c', 'src/config/common.c', 'src/util/paths.c', 'src/runtimes/globals.c',
             'src/nix/config.c', 'src/nix/entrypoint.c', 'src/nix/util.c', 'src/nix/jit_memcpy.c',
             'src/nix/plthook/plthook_osx.c']
    subprocess.run(['clang', '-arch', 'arm64', '-mmacosx-version-min=11.0', '-dynamiclib', '-O2',
                    '-Isrc', *files, str(root / 'src/main_queue.c'), '-o', str(root / 'runtime/libdigfloor.dylib')],
                   cwd=source, check=True)
csc = sorted((sdk / 'sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
output_path = Path(sys.argv[sys.argv.index('--output') + 1]).resolve() if '--output' in sys.argv else root / 'runtime/ValheimBridge.dll'
output_path.parent.mkdir(parents=True, exist_ok=True)
args = [str(sdk / 'dotnet'), str(csc), '-nologo', '-target:library', '-optimize+', '-nostdlib+',
        '-out:' + str(output_path)]
args += ['-r:' + str(p) for p in managed.glob('*.dll')]
args += [str(p) for p in (root / 'src').glob('*.cs')]
subprocess.run(args, check=True)
print('Built', output_path)
