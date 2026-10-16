"""Acquire the pinned .NET SDK from Microsoft's blob endpoint, verifying SHA512.

Range downloads keep acquisition usable on connections with a per-stream cap.
No administrator privileges or machine-wide environment changes are required.
"""
import concurrent.futures
import hashlib
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOWNLOAD = ROOT / '.tools' / 'downloads'
VERSION = '10.0.402'
URL = f'https://dotnetcli.blob.core.windows.net/dotnet/Sdk/{VERSION}/dotnet-sdk-{VERSION}-win-x64.zip'
SIZE = 300755826
SHA512 = 'ae4fd528cb7e9f6af6aaf7de36715a69f761f183744f1bb3025d19f5a92447090ec561410e46266c1d10a63781ed6f3de771760bc76c876d87daabc367b898cd'
CHUNK = 8 * 1024 * 1024


def fetch(part):
    start = part * CHUNK
    end = min(start + CHUNK, SIZE) - 1
    target = DOWNLOAD / f'sdk.part{part:03d}'
    expected = end - start + 1
    if not target.exists() or target.stat().st_size != expected:
        subprocess.run(['curl.exe', '--fail', '--silent', '--show-error', '--location',
                        '--retry', '2', '--retry-all-errors', '--connect-timeout', '15',
                        '--max-time', '300', '--speed-limit', '2048', '--speed-time', '30', '--range', f'{start}-{end}',
                        '--output', str(target), URL], check=True,
                       creationflags=subprocess.CREATE_NO_WINDOW)
    if target.stat().st_size != expected:
        raise RuntimeError(f'Part {part} length mismatch')
    print(f'Part {part + 1}/{(SIZE + CHUNK - 1) // CHUNK} verified', flush=True)
    return target


if __name__ == '__main__':
    DOWNLOAD.mkdir(parents=True, exist_ok=True)
    parts = list(range((SIZE + CHUNK - 1) // CHUNK))
    with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
        results = list(pool.map(fetch, parts))
    target = DOWNLOAD / f'dotnet-sdk-{VERSION}-win-x64.zip'
    with target.open('wb') as output:
        for part in results:
            with part.open('rb') as source:
                shutil.copyfileobj(source, output)
    with target.open('rb') as source:
        digest = hashlib.file_digest(source, 'sha512').hexdigest()
    if digest != SHA512:
        raise RuntimeError('SDK integrity check failed')
    print('Complete SDK verified against Microsoft SHA512 metadata.', flush=True)
