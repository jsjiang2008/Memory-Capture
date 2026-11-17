"""Generate the app's small geometric camera icon using only the standard library."""
from pathlib import Path
import struct
import zlib


def rounded(x, y, left, top, right, bottom, radius):
    cx = min(max(x, left + radius), right - radius)
    cy = min(max(y, top + radius), bottom - radius)
    return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2


def png(size):
    rows = bytearray()
    for y in range(size):
        rows.append(0)
        for x in range(size):
            px, py = (x + .5) * 64 / size, (y + .5) * 64 / size
            color = (79, 70, 229, 255) if rounded(px, py, 1, 1, 63, 63, 15) else (0, 0, 0, 0)
            shell = rounded(px, py, 12, 20, 52, 47, 5)
            inside = rounded(px, py, 15, 23, 49, 44, 2)
            top = rounded(px, py, 22, 15, 40, 24, 3) and not rounded(px, py, 25, 18, 37, 25, 1)
            lens = 7 ** 2 <= (px - 32) ** 2 + (py - 33.5) ** 2 <= 10 ** 2
            if (shell and not inside) or top or lens:
                color = (255, 255, 255, 255)
            rows.extend(color)
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b'')


sizes = (32, 64, 128, 256)
images = [png(size) for size in sizes]
offset = 6 + 16 * len(images)
directory = bytearray(struct.pack('<HHH', 0, 1, len(images)))
for size, image in zip(sizes, images):
    directory.extend(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(image), offset))
    offset += len(image)
target = Path(__file__).resolve().parents[1] / 'src/MemoryCapture/Assets/App.ico'
target.parent.mkdir(parents=True, exist_ok=True)
target.write_bytes(directory + b''.join(images))
