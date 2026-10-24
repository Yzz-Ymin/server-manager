#!/usr/bin/env python3
"""Build the native application with the installed Apple toolchain."""
import json
import os
from pathlib import Path
import platform
import plistlib
import shutil
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parent
APP=ROOT/'dist/ServerManager.app'
VERSION='1.0.0'

def main():
    if platform.system()!='Darwin':raise SystemExit('Building requires macOS.')
    if APP.exists():shutil.rmtree(APP)
    contents=APP/'Contents';binary=contents/'MacOS/ServerManager';scripts=contents/'Resources/scripts'
    binary.parent.mkdir(parents=True);scripts.mkdir(parents=True)
    sdk=os.environ.get('SDKROOT') or subprocess.check_output(['/usr/bin/xcrun','--sdk','macosx','--show-sdk-path'],text=True).strip()
    architecture=os.environ.get('BUILD_ARCH',platform.machine())
    if architecture not in ('x86_64','arm64'):raise SystemExit('BUILD_ARCH must be x86_64 or arm64.')
    with tempfile.TemporaryDirectory(prefix='server-manager-build-') as temporary:
        tmp=Path(temporary)
        command=['/usr/bin/xcrun','swiftc','-swift-version','5','-O','-sdk',sdk,'-target',architecture+'-apple-macosx13.0','-module-cache-path',str(tmp/'modules'),'-debug-prefix-map',str(ROOT)+'=/src/macos']
        # Some Command Line Tools releases ship duplicate Swift bridging module maps.
        developer=Path(subprocess.check_output(['/usr/bin/xcode-select','-p'],text=True).strip())
        maps=developer/'usr/include/swift'
        if all((maps/name).exists() for name in ('module.modulemap','bridging.modulemap')):
            empty=tmp/'empty.modulemap';empty.write_text('')
            overlay=tmp/'overlay.json';overlay.write_text(json.dumps({'version':0,'roots':[{'type':'file','name':str(maps/'module.modulemap'),'external-contents':str(empty)}]}))
            command+=['-vfsoverlay',str(overlay)]
        subprocess.run(command+[str(ROOT/'Source/App.swift'),'-o',str(binary),'-framework','AppKit'],check=True)
    for name in ('server_access.py','native_bridge.py','connection.py'):shutil.copy2(ROOT/'Source'/name,scripts/name)
    shutil.copy2(ROOT/'Assets/AppIcon.icns',contents/'Resources/AppIcon.icns')
    info={'CFBundleIdentifier':'org.servermanager.app','CFBundleName':'ServerManager','CFBundleDisplayName':'服务器管理','CFBundleExecutable':'ServerManager','CFBundleVersion':'1','CFBundleShortVersionString':VERSION,'CFBundlePackageType':'APPL','CFBundleIconFile':'AppIcon','LSUIElement':False,'LSMinimumSystemVersion':'13.0','NSHighResolutionCapable':True,'CFBundleURLTypes':[{'CFBundleURLName':'Server Manager','CFBundleURLSchemes':['server-manager']}]}
    with (contents/'Info.plist').open('wb') as stream:plistlib.dump(info,stream)
    binary.chmod(0o755)
    subprocess.run(['/usr/bin/codesign','--force','--deep','--sign','-',str(APP)],check=True)
    print(APP)

if __name__=='__main__':main()
