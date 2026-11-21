"""Run the installed CubeMX with a separate home and in-memory preferences."""
from pathlib import Path
import argparse,os,subprocess
from settings import ROOT

def cube_directory():
    return Path(os.environ.get('STM32CubeMX_PATH', Path.home()/'AppData/Local/Programs/STM32CubeMX'))

def run(script,timeout=55,home=None):
    install=cube_directory()
    java=install/'jre/bin/java.exe';javac=install/'jre/bin/javac.exe'
    classes=ROOT/'build/java';classes.mkdir(parents=True,exist_ok=True)
    subprocess.run([str(javac),'-d',str(classes),str(ROOT/'tools/WorkspacePreferencesFactory.java')],check=True,capture_output=True)
    sandbox=Path(home) if home else ROOT/'build/sandbox';sandbox.mkdir(parents=True,exist_ok=True)
    args=[str(java),'-Duser.home='+str(sandbox),'-Djava.io.tmpdir='+str(sandbox),
          '-Djava.util.prefs.PreferencesFactory=WorkspacePreferencesFactory','-Dfile.encoding=UTF-8',
          '-cp',str(classes)+os.pathsep+str(install/'STM32CubeMX.exe'),
          'com.st.microxplorer.maingui.STM32CubeMX','-q',str(Path(script).resolve())]
    logs=ROOT/'build/logs';logs.mkdir(parents=True,exist_ok=True)
    try:
        p=subprocess.run(args,cwd=install,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=timeout)
        output=p.stdout.decode('utf-8',errors='replace')
    except subprocess.TimeoutExpired as e:
        output=(e.stdout or b'').decode('utf-8',errors='replace')+'\nTIMEOUT\n'
    (logs/(Path(script).stem+'.log')).write_text(output,encoding='utf-8')
    print('\n'.join(l for l in output.splitlines() if l.strip() and not any(s in l for s in ('[INFO]','[WARN]','[DEBUG]')))[-18000:])
    return output
if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('script');a=ap.parse_args();run(a.script)
