from PIL import Image, ImageDraw
from pathlib import Path
fs=sorted(Path('tmp/pdfs/rendered').glob('page-*.png'))
thumbs=[]
for f in fs:
    im=Image.open(f).convert('RGB')
    im.thumbnail((300,425))
    canvas=Image.new('RGB',(320,455),'white')
    canvas.paste(im,((320-im.width)//2,20))
    ImageDraw.Draw(canvas).text((12,4),f.stem,fill='black')
    thumbs.append(canvas)
out=Image.new('RGB',(960,1820),(220,225,230))
for i,im in enumerate(thumbs):
    out.paste(im,((i%3)*320,(i//3)*455))
out.save('tmp/pdfs/contact.png')
print('made contact',len(fs))


