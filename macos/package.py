#!/usr/bin/env python3
"""Package a built app without local configuration, extended attributes or user metadata."""
import hashlib
import os
from pathlib import Path
import platform
import shutil
import stat
import subprocess
import tempfile
import zipfile

ROOT=Path(__file__).resolve().parents[1]
VERSION='1.0.0'

def archive(target,entries):
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as output:
        for source,name in sorted(entries,key=lambda entry:entry[1]):
            item=zipfile.ZipInfo(name,(2026,1,1,0,0,0));item.create_system=3
            item.compress_type=zipfile.ZIP_DEFLATED
            item.external_attr=(source.stat().st_mode & 0xffff)<<16
            output.writestr(item,source.read_bytes())

def main():
    app=ROOT/'macos/dist/ServerManager.app'
    if not app.is_dir():raise SystemExit('Run macos/build.py first.')
    architecture=os.environ.get('BUILD_ARCH',platform.machine())
    output=ROOT/'packages/macos';output.mkdir(parents=True,exist_ok=True)
    stem=f'ServerManager-{VERSION}-macos-{architecture}'
    archive(output/(stem+'.zip'),[(p,'ServerManager.app/'+str(p.relative_to(app))) for p in app.rglob('*') if p.is_file()])
    with tempfile.TemporaryDirectory(prefix='server-manager-dmg-') as temporary:
        folder=Path(temporary)
        shutil.copytree(app,folder/'ServerManager.app',copy_function=shutil.copyfile)
        # copyfile strips metadata; restore executable permission needed by the app.
        (folder/'ServerManager.app/Contents/MacOS/ServerManager').chmod(0o755)
        (folder/'Applications').symlink_to('/Applications')
        subprocess.run(['/usr/bin/hdiutil','create','-ov','-format','UDZO','-fs','HFS+','-volname','Server Manager','-nospotlight','-noanyowners','-srcowners','off','-srcfolder',str(folder),str(output/(stem+'.dmg'))],check=True)
    entries=[]
    for p in ROOT.rglob('*'):
        relative=p.relative_to(ROOT)
        if not p.is_file() or any(part in ('.git','__pycache__','dist','packages') for part in relative.parts):continue
        entries.append((p,'server-manager/'+str(relative)))
    archive(output/f'ServerManager-{VERSION}-source.zip',entries)
    assets=sorted([p for p in output.iterdir() if p.suffix in ('.zip','.dmg')])
    (output/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in assets))
    print(output)

if __name__=='__main__':main()
