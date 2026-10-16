from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "output" / "pdf" / "TANGERINE_ZIP_Motion_Piano_Jianpu.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

pdfmetrics.registerFont(TTFont("SimHei", r"C:\Windows\Fonts\simhei.ttf"))
pdfmetrics.registerFont(TTFont("Arial", r"C:\Windows\Fonts\arial.ttf"))

# The score uses one flat (F major). The source piano staff is treble clef.
patterns = {
    "A": ["D5", "F5", "A5", "F5", "D5", "A5", "F5", "A5"],
    "B": ["B♭4", "F5", "D5", "F5", "B♭4", "F5", "D5", "F5"],
    "C": ["F5", "A5", "C6", "A5", "F5", "C6", "A5", "C6"],
    "D": ["C5", "G5", "E5", "G5", "C5", "G5", "E5", "G5"],
    "E": ["C5", "E5", "G5", "E5", "C5", "G5", "E5", "G5"],
    "F": ["D5", "A5", "F5", "A5", "D5", "A5", "F5", "A5"],
}

# Bars 1-4 are the introduction; bars 5-32 repeat the four-bar figure.
bars = [None, patterns["B"], patterns["C"], patterns["D"]]
bars += [patterns[k] for k in "ABCD" * 7]
bars += [patterns[k] for k in "ABEF"]
assert len(bars) == 36

degree = {"F": "1", "G": "2", "A": "3", "B♭": "4", "C": "5", "D": "6", "E": "7"}
upper = {"F5", "G5", "A5", "B♭5", "C6", "D6", "E6"}
dynamics = {1: "p", 3: "mf", 9: "mf", 17: "p", 25: "f", 33: "f"}

W, H = A4
c = canvas.Canvas(str(OUT), pagesize=A4)
c.setTitle("TANGERINE ZIP — Motion | Piano Jianpu")
c.setAuthor("Piano part transcribed from supplied score")
ink = HexColor("#1B242A")
muted = HexColor("#56616A")
accent = HexColor("#D67236")


def text_center(value, x, y, font="Arial", size=11, color=ink):
    c.setFillColor(color)
    c.setFont(font, size)
    c.drawCentredString(x, y, value)


def note(pitch, x, y):
    name = pitch[:-1]
    text_center(degree[name], x, y, "Arial", 18)
    if pitch in upper:
        c.setFillColor(ink)
        c.circle(x, y + 23, 1.7, stroke=0, fill=1)


def draw_bar(number, pitches, x0, y, width):
    x1 = x0 + width
    c.setStrokeColor(muted)
    c.setLineWidth(0.75)
    c.line(x0, y - 31, x0, y + 33)
    if number % 3 == 0 or number == 36:
        c.line(x1, y - 31, x1, y + 33)
    c.setFillColor(muted)
    c.setFont("Arial", 9)
    c.drawString(x0 + 8, y + 43, str(number))

    if number == 1:
        # Half rest, followed by two quarter notes.
        xs = [x0 + 25, x0 + 75, x0 + 119, x0 + 151]
        text_center("0", xs[0], y, "Arial", 18)
        c.setStrokeColor(ink)
        c.setLineWidth(1.0)
        c.line(xs[0] + 11, y + 5, xs[1] - 12, y + 5)
        note("D5", xs[2], y)
        note("A5", xs[3], y)
    else:
        pad = 12
        step = (width - 2 * pad) / 8
        xs = [x0 + pad + step * (i + .5) for i in range(8)]
        for pitch, x in zip(pitches, xs):
            note(pitch, x, y)
        # Two groups of four eighth notes, in standard jianpu notation.
        c.setStrokeColor(ink)
        c.setLineWidth(1.35)
        for start in (0, 4):
            c.line(xs[start] - 7, y - 14, xs[start + 3] + 7, y - 14)

    if number in dynamics:
        text_center(dynamics[number], x0 + 23, y - 43, "Arial", 10, muted)


for page in range(2):
    c.setFillColor(ink)
    c.setFont("Arial", 18)
    c.drawString(44, H - 55, "TANGERINE ZIP — Motion")
    c.setFillColor(accent)
    c.setFont("SimHei", 16)
    c.drawRightString(W - 44, H - 55, "钢琴简谱")
    c.setStrokeColor(accent)
    c.setLineWidth(1.6)
    c.line(44, H - 66, W - 44, H - 66)
    c.setFillColor(muted)
    c.setFont("Arial", 10)
    c.drawString(44, H - 89, "1 = F4    4/4    Quarter note = 144")
    c.setFont("SimHei", 9)
    c.drawRightString(W - 44, H - 89, "上方圆点表示高八度；下划线表示八分音符")

    left, right = 44, W - 44
    bar_width = (right - left) / 3
    for row in range(6):
        y = H - 157 - row * 112
        for col in range(3):
            number = page * 18 + row * 3 + col + 1
            draw_bar(number, bars[number - 1], left + col * bar_width, y, bar_width)

    c.setStrokeColor(HexColor("#D6DADC"))
    c.setLineWidth(.6)
    c.line(44, 55, W - 44, 55)
    c.setFillColor(muted)
    c.setFont("Arial", 8)
    c.drawString(44, 40, "Piano ostinato · bars 1–36 · transcribed from supplied score")
    c.drawRightString(W - 44, 40, f"{page + 1} / 2")
    c.showPage()

c.save()
print(OUT)
