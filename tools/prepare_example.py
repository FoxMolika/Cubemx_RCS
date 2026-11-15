"""Export a standalone .c/.h example. Does not edit any existing project."""
import argparse
from settings import ROOT,DEFAULTS,load
from configurator import export

def prepare(out=None,values=None):
    return export(values if values is not None else DEFAULTS,out or ROOT/'generated/chassis')

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--out');ap.add_argument('--config');args=ap.parse_args()
    print(prepare(args.out,load(args.config) if args.config else None))
