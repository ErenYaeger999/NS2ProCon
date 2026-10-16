"""Render the app's own vector-like gamepad mark to a multi-resolution ICO."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
image = Image.new('RGBA', (1024, 1024))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((32, 32, 992, 992), radius=245, fill='#187B78')
draw.rounded_rectangle((195, 332, 829, 722), radius=170, fill='#FFFFFF')
draw.rounded_rectangle((291, 441, 333, 593), radius=15, fill='#187B78')
draw.rounded_rectangle((236, 496, 388, 538), radius=15, fill='#187B78')
for x, y in [(690, 447), (745, 502), (690, 557), (635, 502)]:
    draw.ellipse((x - 25, y - 25, x + 25, y + 25), fill='#187B78')
draw.rounded_rectangle((457, 506, 493, 522), radius=8, fill='#B1D4D0')
draw.rounded_rectangle((530, 506, 566, 522), radius=8, fill='#B1D4D0')
target = root / 'src/NS2Pro.App/Assets'
target.mkdir(parents=True, exist_ok=True)
image.save(target / 'App.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
image.resize((256, 256), Image.Resampling.LANCZOS).save(target / 'App.png')
print(target / 'App.ico')
