"""Check generated C structure, not algorithm similarity or user-owned code.

Comments and C string/character literals are lexed before counting statements.
USER CODE bodies and machine checksum lines are deliberately exempt.
"""
from pathlib import Path
import re,sys
ROOT=Path(__file__).resolve().parents[1]
CORE=['rcs_chassis.c','rcs_chassis.h','rcs_control.c','rcs_control.h','rcs_hal.c','rcs_hal.h']
ROUTE=['rcs_nav.c','rcs_nav.h','rcs_route.c','rcs_route.h','rcs_actions.c','rcs_actions.h']
MAX_LINE=110
MARKER=re.compile(r'^/\* (?:RCS REGION DEFAULT |RCS GENERATED SHA256 |USER CODE (?:BEGIN|END) )')

def user_lines(text):
    active=None;skip=set()
    for number,line in enumerate(text.splitlines(),1):
        start=re.fullmatch(r'/\* USER CODE BEGIN ([A-Z0-9_]+) \*/',line)
        end=re.fullmatch(r'/\* USER CODE END ([A-Z0-9_]+) \*/',line)
        if start:
            if active:raise ValueError('nested USER CODE marker')
            active=start.group(1)
        elif end:
            if active!=end.group(1):raise ValueError('unmatched USER CODE marker')
            active=None
        elif active:skip.add(number)
    if active:raise ValueError('unclosed USER CODE marker')
    return skip

def code_mask(text,strings=True):
    """Keep offsets/newlines while masking comments, and optionally literals."""
    out=list(text);i=0;state='code';quote=''
    while i<len(text):
        c=text[i];n=text[i:i+2]
        if state=='block':
            if n=='*/':out[i]=out[i+1]=' ';i+=2;state='code';continue
            if c!='\n':out[i]=' '
        elif state=='line':
            if c=='\n':state='code'
            else:out[i]=' '
        elif state=='literal':
            if strings and c!='\n':out[i]=' '
            if c=='\\' and i+1<len(text):
                if strings and text[i+1]!='\n':out[i+1]=' '
                i+=2;continue
            if c==quote:state='code'
        elif n in ['//','/*']:
            out[i]=out[i+1]=' ';state='line' if n=='//' else 'block';i+=2;continue
        elif c in ['"',"'"]:
            quote=c;state='literal'
            if strings:out[i]=' '
        i+=1
    return ''.join(out)

def check_source(name,text):
    problems=[];text=text.lstrip('\ufeff').replace('\r\n','\n').replace('\r','\n')
    try:skip=user_lines(text)
    except ValueError as e:return [name+': '+str(e)]
    raw=text.splitlines()
    skeleton=''.join(''.join('\n' if c=='\n' else ' ' for c in line) if n in skip else line for n,line in enumerate(text.splitlines(keepends=True),1))
    masked=code_mask(skeleton);code=masked.splitlines();visible=code_mask(skeleton,False)
    header=re.match(r'/\*\*(.*?)\*/',text,re.S)
    if not header or not re.search(r'@file\s+'+re.escape(name)+r'\b',header.group(1)) or '@brief' not in header.group(1):problems.append(name+': missing matching @file/@brief file header')
    parens=[];last_word='';pending=False;macro=False
    for number,line in enumerate(raw,1):
        if number in skip:continue
        if MARKER.match(line):continue
        if '\t' in line:problems.append(f'{name}:{number}: TAB outside USER CODE')
        if len(line)>MAX_LINE:problems.append(f'{name}:{number}: {len(line)} characters exceeds {MAX_LINE}')
        if line!=line.rstrip():problems.append(f'{name}:{number}: trailing whitespace')
        c=code[number-1] if number<=len(code) else '';stripped=c.strip()
        if not stripped:continue
        is_preprocessor=c.lstrip().startswith('#') or macro;macro=is_preprocessor and line.rstrip().endswith('\\')
        if is_preprocessor:continue
        continuation=bool(parens) or pending
        indent=len(c)-len(c.lstrip())
        if not continuation and indent%4:problems.append(f'{name}:{number}: block indentation must use 4 spaces')
        count=0
        for token in re.findall(r'[A-Za-z_]\w*|[^\s]',c):
            if token=='(':parens.append(last_word=='for')
            elif token==')':
                if parens:parens.pop()
            elif token==';' and not any(parens):count+=1
            if token not in ['(',')']:last_word=token
        if count>1:problems.append(f'{name}:{number}: multiple statements on one line')
        if stripped.endswith((';','{','}')):pending=False
        elif re.match(r'^(?:if|for|while|switch)\s*\(',stripped) and stripped.endswith(')'):pending=False
        else:pending=True
    # Plain generated C APIs, with arbitrary return types and pointer qualifiers.
    function=re.compile(r'(?m)^((?:[A-Za-z_]\w*[ \t*]+)+)([A-Za-z_]\w*)\s*\([^;{}]*\)\s*([;{])')
    functions=[]
    for m in function.finditer(masked):
        if m.group(1).split()[0] in ['typedef','return','if','for','while','switch']:continue
        number=masked.count('\n',0,m.start())+1
        if number in skip:continue
        functions.append(m)
        before=text[:m.start()].rstrip();start=before.rfind('/**')
        if not before.endswith('*/') or start<0 or '@brief' not in before[start:] or '@file' in before[start:]:problems.append(f'{name}:{number}: {m.group(2)} lacks its own adjacent Doxygen comment')
    if name.endswith('.h'):
        if not re.search(r'(?m)^#pragma\s+once\s*$',masked):problems.append(name+': missing #pragma once')
        if re.search(r'(?m)^#ifndef\s+RCS_\w+_H\b',masked):problems.append(name+': legacy include guard')
        if functions and not re.search(r'#ifdef\s+__cplusplus\s+extern\s+"C"\s*\{',visible):problems.append(name+': missing guarded extern "C"')
    elif name.endswith('.c'):
        match=re.search(r'(?m)^#include\s+"([^"]+)"',visible)
        if not match or match.group(1)!=name[:-2]+'.h':problems.append(name+': first include must be own header')
    return problems

def check_files(files):
    return [problem for name,text in files.items() for problem in check_source(name,text)]

def check(folder):
    folder=Path(folder);return check_files({name:(folder/name).read_text(encoding='utf-8-sig') for name in CORE})

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    if len(sys.argv)>1:
        folder=Path(sys.argv[1]);names=CORE+['rcs_config.h'] if (folder/'rcs_chassis.c').exists() else ROUTE
        files={name:(folder/name).read_text(encoding='utf-8-sig') for name in names}
    else:
        from settings import config_header,DEFAULTS
        files={name:(ROOT/'firmware'/name).read_text(encoding='utf-8-sig') for name in CORE}
        files.update({name:(ROOT/'map_editor/firmware'/name).read_text(encoding='utf-8-sig') for name in ['rcs_nav.c','rcs_nav.h']})
        files['rcs_config.h']=config_header(DEFAULTS)
    errors=check_files(files)
    if errors:print('\n'.join(errors));return 1
    print('PASS generated C style:',len(files),'files; USER CODE bodies preserved/exempt');return 0
if __name__=='__main__':sys.exit(main())
