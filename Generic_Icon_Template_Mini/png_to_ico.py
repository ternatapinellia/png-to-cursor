from __future__ import annotations
import argparse
from pathlib import Path
from PIL import Image

NAME_MAP = {
    'ThisPC': 'ThisPC',
    'RecycleBinFull': 'RecycleBinFull',
    'RecycleBinEmpty': 'RecycleBinEmpty',
    'Network': 'Network',
    'Documents': 'Documents',
    'Folder': 'Folder',
    'FolderEmpty': 'FolderEmpty',
    'PS': 'PS',
    'QQ': 'QQ',
    'Browser': 'Browser',
}
SIZES=((16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256))

def trimmed(im):
    im=im.convert('RGBA'); box=im.getbbox(); return im.crop(box) if box else im

def square(im,size=256):
    im=trimmed(im); scale=min(size/im.width,size/im.height); w=max(1,round(im.width*scale)); h=max(1,round(im.height*scale)); im=im.resize((w,h),Image.Resampling.LANCZOS); c=Image.new('RGBA',(size,size),(0,0,0,0)); c.alpha_composite(im,((size-w)//2,(size-h)//2)); return c

def one(src,dst):
    im=square(Image.open(src)); dst.parent.mkdir(parents=True,exist_ok=True); im.save(dst,format='ICO',sizes=SIZES)

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('source',type=Path); ap.add_argument('--out',type=Path); a=ap.parse_args(); s=a.source.resolve()
    if s.is_dir():
      out=a.out or s.parent/(s.name+'_icons'); out.mkdir(parents=True,exist_ok=True)
      for p in sorted(s.glob('*.png')):
        dst=out/(NAME_MAP.get(p.stem,p.stem)+'.ico')
        one(p,dst); print('ICO',p.name,'->',dst)
    else:
      out=a.out or s.with_suffix('.ico'); one(s,out); print('ICO',s.name,'->',out)
if __name__=='__main__': main()
