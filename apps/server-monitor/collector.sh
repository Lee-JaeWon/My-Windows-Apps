python3 -u - <<'PY'
import csv
import io
import json
import subprocess
import time

def number(value):
    try:
        return int(value.strip())
    except (ValueError, TypeError):
        return None

deadline = time.monotonic()
while True:
    sample = {"gpus": [], "gpu_error": "", "ram_error": ""}
    try:
        memory = {}
        with open('/proc/meminfo') as handle:
            for line in handle:
                key, value = line.split(':', 1)
                memory[key] = int(value.split()[0])
        sample['ram_total'] = memory['MemTotal'] / 1024.0
        sample['ram_used'] = (memory['MemTotal'] - memory['MemAvailable']) / 1024.0
    except Exception:
        sample['ram_error'] = 'RAM information unavailable'
    try:
        output = subprocess.check_output([
            'nvidia-smi', '--query-gpu=index,name,memory.used,memory.total,utilization.gpu,temperature.gpu',
            '--format=csv,noheader,nounits'
        ], stderr=subprocess.DEVNULL, timeout=3, universal_newlines=True)
        for row in csv.reader(io.StringIO(output)):
            if len(row) == 6:
                sample['gpus'].append({
                    'index': row[0].strip(), 'name': row[1].strip(),
                    'used': number(row[2]), 'total': number(row[3]), 'utilization': number(row[4]),
                    'temperature': number(row[5])
                })
    except FileNotFoundError:
        sample['gpu_error'] = 'nvidia-smi not found'
    except subprocess.TimeoutExpired:
        sample['gpu_error'] = 'GPU query timed out'
    except Exception:
        sample['gpu_error'] = 'GPU query failed'
    try:
        print(json.dumps(sample, separators=(',', ':')), flush=True)
    except (BrokenPipeError, OSError):
        break
    deadline += 1.0
    now = time.monotonic()
    if deadline < now:
        deadline = now
    time.sleep(max(0.0, deadline - now))
PY
