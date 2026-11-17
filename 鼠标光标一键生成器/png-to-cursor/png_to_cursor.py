from __future__ import annotations
import argparse, struct, sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print('缺少 Pillow。请运行: python -m pip install pillow')
    raise SystemExit(2)

ROOT_HOTSPOTS = {'normal':(0,0),'arrow':(0,0),'pointer':(0,0),'help':(0,0),'question':(0,0),'working':(0,0),'busy':(14,16),'wait':(14,16),'precision':(5,5),'cross':(5,5),'text':(0,18),'ibeam':(0,18),'pen':(0,18),'hand':(0,18),'link':(0,0),'unavailable':(15,15),'no':(15,15),'move':(15,15),'moveall':(15,15),'vert':(4,15),'vertical':(4,15),'horz':(15,27),'horizontal':(15,27),'diag1':(15,15),'diag2':(15,15),'diagonal':(15,15),'alt':(0,0),'alternate':(0,0)}

def trim(im):
    box=im.getbbox()
    return im.crop(box) if box else im

def hot(name, im, size):
    key=name.lower().replace('-','').replace('_','')
    for k,v in ROOT_HOTSPOTS.items():
        if key.startswith(k):
            sx=size/32; return round(v[0]*sx), round(v[1]*sx)
    box=im.getbbox() or (0,0,im.width,im.height)
    x=box[0]; y=box[1]
    return round(x*size/im.width), round(y*size/im.height)

def load(p,size):
    im=Image.open(p).convert('RGBA'); im=trim(im)
    scale=min(size/im.width,size/im.height); nw=max(1,round(im.width*scale)); nh=max(1,round(im.height*scale))
    im=im.resize((nw,nh),Image.Resampling.LANCZOS)
    c=Image.new('RGBA',(size,size),(0,0,0,0)); c.alpha_composite(im,((size-nw)//2,0)); return c

def payload(im):
    w,h=im.size; maskstride=((w+31)//32)*4; xor=bytearray(); mask=bytearray(maskstride*h); px=im.load()
    for y in range(h-1,-1,-1):
        for x in range(w):
            r,g,b,a=px[x,y]; xor.extend((b,g,r,a))
            if a==0: mask[(h-1-y)*maskstride+x//8] |= 1<<(7-x%8)
    hdr=struct.pack('<IiiHHIIiiII',40,w,h*2,1,32,0,len(xor)+len(mask),0,0,0,0)
    return hdr+bytes(xor)+bytes(mask)

def cur(im,hx,hy):
    data=payload(im); return struct.pack('<HHHBBBBHHII',0,2,1,im.width,im.height,0,0,hx,hy,len(data),22)+data

def chunk(tag,data):
    out=tag+struct.pack('<I',len(data))+data
    return out+(bytes(1) if len(data)&1 else b'')

def ani(frames,hx,hy,delay=6):
    icons=[chunk(b'icon',cur(f,hx,hy)) for f in frames]; w,h=frames[0].size
    ah=struct.pack('<IIIIIIIII',36,len(frames),len(frames),w,h,32,1,delay,1)
    rate=struct.pack('<'+'I'*len(frames),*[delay]*len(frames)); fd=b'fram'+b''.join(icons); body=chunk(b'anih',ah)+chunk(b'rate',rate)+b'LIST'+struct.pack('<I',len(fd))+fd
    return b'RIFF'+struct.pack('<I',4+len(body))+b'ACON'+body

def run(src,out,size,delay,forcehot=None):
    out.mkdir(parents=True,exist_ok=True); root=sorted(p for p in src.glob('*.png') if p.is_file()); subs=sorted(p for p in src.iterdir() if p.is_dir())
    for p in root:
        im=load(p,size); hx,hy=forcehot or hot(p.stem,Image.open(p),size); (out/(p.stem+'.cur')).write_bytes(cur(im,hx,hy)); print('CUR',p.name,hx,hy)
    for d in subs:
        ps=sorted(p for p in d.rglob('*.png') if p.is_file())
        if not ps: continue
        frames=[load(p,size) for p in ps]; hx,hy=forcehot or hot(d.name,Image.open(ps[0]),size); (out/(d.name+'.ani')).write_bytes(ani(frames,hx,hy,delay)); print('ANI',d.name,len(frames),hx,hy)

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('source',nargs='?',type=Path); ap.add_argument('--out',type=Path); ap.add_argument('--size',type=int,default=32); ap.add_argument('--delay',type=int,default=6); ap.add_argument('--hotspot-x',type=int); ap.add_argument('--hotspot-y',type=int); a=ap.parse_args(); default=Path(__file__).resolve().parent/'PNG'; bundled=default if default.is_dir() and any(default.rglob('*.png')) else None; src=(a.source or bundled or Path.home()/'Downloads').resolve(); out=a.out or src.parent/(src.name+'_converted'); force=(a.hotspot_x,a.hotspot_y) if a.hotspot_x is not None and a.hotspot_y is not None else None; run(src,out.resolve(),a.size,a.delay,force)
if __name__=='__main__': main()
