"""Download pinned local inference engines and models; verify every SHA-256.

No administrator rights, Python packages, CUDA toolkit or cloud account required.
"""
import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(os.environ['USERPROFILE']) / 'Applications' / 'LazyType'
ASSETS = [
    ('models/silero-vad.bin',
     'https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin',
     '2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987', None),
    ('models/whisper-turbo-q5.bin',
     'https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin',
     '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2', None),
    ('downloads/whisper-b5130-cuda12.zip',
     'https://github.com/ggml-org/whisper.cpp/releases/download/b5130/whisper-cublas-12.4.0-bin-x64.zip',
     'af520ddd034d985b55dfeea3e465ed93653ba2aee1a55e865033edc548c272a7', 'engines/whisper'),
    ('downloads/llama-b11146-cuda12.zip',
     'https://github.com/ggml-org/llama.cpp/releases/download/b11146/llama-b11146-bin-win-cuda-12.4-x64.zip',
     '3c806a6ceccc3dae1c743ceb1a1fb2cce5b76f40bfbd4c6b7b8afb6ef45a5807', 'engines/llama'),
    ('downloads/llama-b11146-cudart.zip',
     'https://github.com/ggml-org/llama.cpp/releases/download/b11146/cudart-llama-bin-win-cuda-12.4-x64.zip',
     '8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6', 'engines/llama'),
]
TEXT_ASSETS = {
    'qwen35': ('models/qwen3.5-9b-q4.gguf',
        'https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/3885219b6810b007914f3a7950a8d1b469d598a5/Qwen3.5-9B-Q4_K_M.gguf',
        '03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8', None),
    'gemma': ('models/gemma4-12b-q4.gguf',
        'https://huggingface.co/unsloth/gemma-4-12b-it-GGUF/resolve/fc034cfff751157913579611efad8462ac1be606/gemma-4-12b-it-Q4_K_M.gguf',
        '0a270ec9fe6b34f4a0d33992b6135117b484ebc4766ab76b51d4ae8c457e4c42', None),
}

def assets_for(choice='qwen35'):
    selected = {'qwen35': ['qwen35'], 'gemma': ['gemma'], 'all': ['qwen35', 'gemma']}[choice]
    return ASSETS + [TEXT_ASSETS[name] for name in selected]

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()

def download(asset):
    name, url, expected, destination = asset
    path = ROOT / name
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists() and asset in TEXT_ASSETS.values():
        # Reuse verified benchmark weights without a second download or copy.
        filename = url.rsplit('/', 1)[1]
        for existing in sorted((ROOT / 'benchmarks').glob('*/models/' + filename), reverse=True):
            if digest(existing) == expected:
                reused = (existing.relative_to(ROOT).as_posix(), url, expected, destination)
                print('Verified existing ' + reused[0], flush=True)
                return reused
    if not path.exists() or digest(path) != expected:
        partial = Path(str(path) + '.partial')
        print('Downloading ' + name, flush=True)
        subprocess.run(['curl.exe', '--fail', '--location', '--retry', '5',
                        '--retry-delay', '3', '--connect-timeout', '30',
                        '--continue-at', '-', '--silent', '--show-error',
                        '--output', str(partial), url], check=True)
        if digest(partial) != expected:
            raise RuntimeError('SHA-256 mismatch: ' + name)
        partial.replace(path)
    print('Verified ' + name, flush=True)
    return asset

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--text-model', choices=['qwen35', 'gemma', 'all'], default='qwen35')
    parser.add_argument('--list', action='store_true', help='List selected pinned assets without downloading')
    options = parser.parse_args()
    assets = assets_for(options.text_model)
    if options.list:
        print(json.dumps([{'file': a[0], 'source': a[1], 'sha256': a[2]} for a in assets], indent=2))
        return
    ROOT.mkdir(parents=True, exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        installed = list(pool.map(download, assets))
    # Extract sequentially: llama and its CUDA runtime share a destination.
    for name, url, expected, destination in installed:
        if destination:
            target = (ROOT / destination).resolve()
            target.mkdir(parents=True, exist_ok=True)
            with zipfile.ZipFile(ROOT / name) as archive:
                for member in archive.infolist():
                    if not (target / member.filename).resolve().is_relative_to(target):
                        raise RuntimeError('Archive path outside destination')
                archive.extractall(target)
            print('Installed ' + destination, flush=True)
    manifest_path = ROOT / 'models-manifest.json'
    previous = json.loads(manifest_path.read_text()) if manifest_path.exists() else []
    manifest = {a['file']: a for a in previous}
    manifest.update({a[0]: {'file': a[0], 'source': a[1], 'sha256': a[2]} for a in installed})
    manifest_path.write_text(json.dumps(list(manifest.values()), indent=2))
    print('Local models and CUDA engines ready: ' + str(ROOT), flush=True)

if __name__ == '__main__':
    main()
