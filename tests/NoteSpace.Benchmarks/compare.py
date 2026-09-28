"""Compare same-runner microbenchmarks; do not turn noisy wall-clock ratios into CI gates."""
import json
import sys
from pathlib import Path

baseline, current = (json.loads(Path(name).read_text()) for name in sys.argv[1:3])
old = {item['name']: item for item in baseline['results']}
lines = ['# NoteSpace CPU microbenchmarks', '', f"Baseline: `{baseline['label']}`. Current: `{current['label']}`.", '', current['note'], '',
         '| Case | Before ms | After ms | Ratio | Before managed bytes | After managed bytes |',
         '| --- | ---: | ---: | ---: | ---: | ---: |']
for item in current['results']:
    before = old[item['name']]
    ratio = before['medianMilliseconds'] / max(item['medianMilliseconds'], 1e-9)
    lines.append(f"| {item['name']} | {before['medianMilliseconds']:.4f} | {item['medianMilliseconds']:.4f} | {ratio:.2f}x | {before['medianManagedBytes']:,} | {item['medianManagedBytes']:,} |")
lines += ['', '## Retained history after the edit benchmark', '',
          f"Before: {baseline['history']}. After: {current['history']}.", '',
          'The history number measures serialized UTF-16 characters, not process memory. The current page transaction still validates and serializes the entire workspace before committing.', '',
          'Ratios compare medians in this runner and can vary on different machines. They do not measure browser startup, end-to-end input latency, GPU time, or Microsoft OneNote.']
Path(sys.argv[3]).write_text('\n'.join(lines) + '\n')
print('\n'.join(lines))
